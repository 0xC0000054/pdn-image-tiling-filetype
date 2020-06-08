////////////////////////////////////////////////////////////////////////////////////
//
// This file is part of pdn-image-tiling-filetype, a FileType plugin for Paint.NET
// that splits an image into tiles and saves them into a zip file.
//
// Copyright (c) 2013, 2017, 2020 Nicholas Hayes
//
// This file is licensed under the MIT License.
// See LICENSE.txt for complete licensing and attribution information.
//
////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Reflection;

namespace TileImageFileType
{
    public sealed class PluginSupportInfo : PaintDotNet.IPluginSupportInfo
    {
        public string Author
        {
            get
            {
                return "null54";
            }
        }

        public string Copyright
        {
            get
            {
                return ((AssemblyCopyrightAttribute)typeof(TileImageFileType).Assembly.GetCustomAttributes(typeof(AssemblyCopyrightAttribute), false)[0]).Copyright;
            }
        }

        public string DisplayName
        {
            get
            {
                return TileImageFileType.StaticName;
            }
        }

        public Version Version
        {
            get
            {
                return typeof(TileImageFileType).Assembly.GetName().Version;
            }
        }

        public Uri WebsiteUri
        {
            get
            {
                return new Uri("http://www.getpaint.net/redirect/plugins.html");
            }
        }

    }
}
