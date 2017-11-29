////////////////////////////////////////////////////////////////////////////////////
//
// This file is part of pdn-image-tiling-filetype, a FileType plugin for Paint.NET
// that splits an image into tiles and saves them into a zip file.
//
// Copyright (c) 2013 Nicholas Hayes
//
// This file is licensed under the MIT License.
// See LICENSE.txt for complete licensing and attribution information.
//
////////////////////////////////////////////////////////////////////////////////////

// Portions of this file has been adapted from:
/////////////////////////////////////////////////////////////////////////////////
// Paint.NET                                                                   //
// Copyright (C) dotPDN LLC, Rick Brewster, Tom Jackson, and contributors.     //
// Portions Copyright (C) Microsoft Corporation. All Rights Reserved.          //
// See src/Resources/Files/License.txt for full licensing and attribution      //
// details.                                                                    //
// .                                                                           //
/////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Ionic.Zip;
using PaintDotNet;
using PaintDotNet.IndirectUI;
using PaintDotNet.PropertySystem;


namespace TileImageFileType
{
    public sealed class TileImageFileType : PropertyBasedFileType, IFileTypeFactory
    {
        private enum SavableBitDepths
        {
            Rgba32, // 2^24 colors, plus a full 8-bit alpha channel
            Rgb24,  // 2^24 colors
            Rgb8,   // 256 colors
            Rgba8   // 255 colors + 1 transparent
        }

        internal static string StaticName
        {
            get
            {
                return "Image Tiles";
            }
        }

        public TileImageFileType() : base(StaticName, FileTypeFlags.SupportsSaving | FileTypeFlags.SavesWithProgress, new string[] { ".zip" })
        {
        }

        protected override bool IsReflexive(PropertyBasedSaveConfigToken token)
        {
            return true;
        }

        private enum PropertyNames
        {
            TileSize
        }

        public override PropertyCollection OnCreateSavePropertyCollection()
        {
            List<Property> props = new List<Property> {
                new Int32Property(PropertyNames.TileSize, 256, 8, 2048)
            };

            return new PropertyCollection(props);
        }

        public override ControlInfo OnCreateSaveConfigUI(PropertyCollection props)
        {
            ControlInfo info = CreateDefaultSaveConfigUI(props);

            info.SetPropertyControlValue(PropertyNames.TileSize, ControlInfoPropertyNames.DisplayName, "Tile size");

            return info;
        }

        protected override Document OnLoad(Stream input)
        {
            throw new NotImplementedException();
        }

        private unsafe void SquishSurfaceTo24Bpp(Surface surface)
        {
            byte* dst = (byte*)surface.GetRowAddress(0);
            int byteWidth = surface.Width * 3;
            int stride24bpp = (byteWidth + 3) & ~3;
            int delta = stride24bpp - byteWidth;

            for (int y = 0; y < surface.Height; ++y)
            {
                ColorBgra* src = surface.GetRowAddress(y);
                ColorBgra* srcEnd = src + surface.Width;

                while (src < srcEnd)
                {
                    dst[0] = src->B;
                    dst[1] = src->G;
                    dst[2] = src->R;
                    ++src;
                    dst += 3;
                }

                dst += delta;
            }
        }

        private unsafe Bitmap CreateAliased24BppBitmap(Surface surface)
        {
            int stride = surface.Width * 3;
            int realStride = (stride + 3) & ~3;
            return new Bitmap(surface.Width, surface.Height, realStride, PixelFormat.Format24bppRgb, new IntPtr(surface.Scan0.VoidStar));
        }

        private unsafe void Analyze(Surface scratchSurface, Rectangle roi, out bool allOpaque, out bool all0or255Alpha, out int uniqueColorCount)
        {
            allOpaque = true;
            all0or255Alpha = true;
            HashSet<uint> uniqueColors = new HashSet<uint>();

            for (int y = roi.Top; y < roi.Bottom; ++y)
            {
                ColorBgra* srcPtr = scratchSurface.GetPointAddressUnchecked(roi.Left, y);
                ColorBgra* endPtr = srcPtr + roi.Width;

                while (srcPtr < endPtr)
                {

                    if (srcPtr->A != 255)
                    {
                        allOpaque = false;
                    }

                    if (srcPtr->A > 0 && srcPtr->A < 255)
                    {
                        all0or255Alpha = false;
                    }

                    if ((srcPtr->A == 255 && uniqueColors.Count < 300) && !uniqueColors.Contains(srcPtr->Bgra))
                    {
                        uniqueColors.Add(srcPtr->Bgra);
                    }

                    ++srcPtr;
                }
            }

            uniqueColorCount = uniqueColors.Count;
        }

        private SavableBitDepths GetBitDepth(Surface scratchSurface, Rectangle bounds)
        {
            bool allOpaque;
            bool all0or255Alpha;
            int uniqueColorCount;

            Analyze(scratchSurface, bounds, out allOpaque, out all0or255Alpha, out uniqueColorCount);

            SavableBitDepths bitDepth = SavableBitDepths.Rgba32;

            if (allOpaque)
            {
                bitDepth = SavableBitDepths.Rgb24;

                if (uniqueColorCount <= 256)
                {
                    bitDepth = SavableBitDepths.Rgb8;
                }
            }
            else if (all0or255Alpha && uniqueColorCount < 256)
            {
                bitDepth = SavableBitDepths.Rgba8;
            }

            // if bit depth is 24 or 8, then we have to do away with the alpha channel
            // for 8-bit, we must have pixels that have either 0 or 255 alpha
            if (bitDepth == SavableBitDepths.Rgb8 ||
                bitDepth == SavableBitDepths.Rgba8 ||
                bitDepth == SavableBitDepths.Rgb24)
            {
                UserBlendOps.NormalBlendOp blendOp = new UserBlendOps.NormalBlendOp();

                unsafe
                {
                    for (int y = bounds.Top; y < bounds.Bottom; ++y)
                    {
                        ColorBgra* srcPtr = scratchSurface.GetPointAddressUnchecked(bounds.Left, y);
                        ColorBgra* endPtr = srcPtr + bounds.Width;

                        while (srcPtr < endPtr)
                        {
                            if (srcPtr->A < 128 && bitDepth == SavableBitDepths.Rgba8)
                            {
                                *srcPtr = ColorBgra.FromBgra(0, 0, 0, 0);
                            }
                            else
                            {
                                *srcPtr = blendOp.Apply(ColorBgra.White, *srcPtr);
                            }

                            srcPtr++;
                        }
                    }
                }
            }


            return bitDepth;
        }

        private static ImageCodecInfo GetImageCodecInfo(ImageFormat format)
        {
            ImageCodecInfo[] encoders = ImageCodecInfo.GetImageEncoders();

            foreach (ImageCodecInfo icf in encoders)
            {
                if (icf.FormatID == format.Guid)
                {
                    return icf;
                }
            }

            return null;
        }

        protected override void OnSaveT(Document input, Stream output, PropertyBasedSaveConfigToken token, Surface scratchSurface, ProgressEventHandler callback)
        {
            int width = input.Width;
            int height = input.Height;

            int tileSize = token.GetProperty<Int32Property>(PropertyNames.TileSize).Value;

            using (RenderArgs args = new RenderArgs(scratchSurface))
            {
                input.Render(args, true);
            }

            List<Rectangle> rects = new List<Rectangle>();

            for (int y = 0; y < height; y += tileSize)
            {
                for (int x = 0; x < width; x += tileSize)
                {
                    Rectangle bounds = new Rectangle(x, y, Math.Min(tileSize, width - x), Math.Min(tileSize, height - y));
                    rects.Add(bounds);
                }
            }

            double progressPercentage = 0.0;
            double progressDelta = (1.0 / rects.Count) * 100.0;

            using (ZipOutputStream zipStream = new ZipOutputStream(output, true))
            {
                int count = rects.Count;

                ImageCodecInfo codec = GetImageCodecInfo(ImageFormat.Png);
                EncoderParameters encoderParams = new EncoderParameters(1);

                for (int i = 0; i < count; i++)
                {
                    Rectangle bounds = rects[i];

                    SavableBitDepths bitDepth = GetBitDepth(scratchSurface, bounds);

                    zipStream.PutNextEntry(string.Format("Image{0}.png", (i + 1)));
                    if (bitDepth == SavableBitDepths.Rgba32)
                    {
                        encoderParams.Param[0] = new EncoderParameter(Encoder.ColorDepth, 32);

                        using (Bitmap temp = scratchSurface.CreateAliasedBitmap(bounds, true))
                        {
                            temp.Save(zipStream, codec, encoderParams);
                        }
                    }
                    else if (bitDepth == SavableBitDepths.Rgb24)
                    {
                        encoderParams.Param[0] = new EncoderParameter(Encoder.ColorDepth, 24);

                        using (Surface surface = new Surface(bounds.Width, bounds.Height))
                        {
                            surface.CopySurface(scratchSurface, bounds); // Make a new surface so we do not clobber the stride of the scratchSurface.
                            SquishSurfaceTo24Bpp(surface);

                            using (Bitmap temp = CreateAliased24BppBitmap(surface))
                            {
                                temp.Save(zipStream, codec, encoderParams);
                            }
                        }
                    }
                    else if (bitDepth == SavableBitDepths.Rgb8)
                    {
                        encoderParams.Param[0] = new EncoderParameter(Encoder.ColorDepth, 8);

                        using (Surface surface = scratchSurface.CreateWindow(bounds))
                        {
                            using (Bitmap temp = Quantize(surface, 7, 256, false, null))
                            {
                                temp.Save(zipStream, codec, encoderParams);
                            }
                        }
                    }
                    else if (bitDepth == SavableBitDepths.Rgba8)
                    {
                        encoderParams.Param[0] = new EncoderParameter(Encoder.ColorDepth, 8);

                        using (Surface surface = scratchSurface.CreateWindow(bounds))
                        {
                            using (Bitmap temp = Quantize(surface, 7, 256, true, null))
                            {
                                temp.Save(zipStream, codec, encoderParams);
                            }
                        }
                    }

                    progressPercentage += progressDelta;

                    callback(this, new ProgressEventArgs(progressPercentage));
                }
            }

        }

        public FileType[] GetFileTypeInstances()
        {
            return new FileType[] { new TileImageFileType() };
        }
    }
}
