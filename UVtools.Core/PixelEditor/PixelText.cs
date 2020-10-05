﻿/*
 *                     GNU AFFERO GENERAL PUBLIC LICENSE
 *                       Version 3, 19 November 2007
 *  Copyright (C) 2007 Free Software Foundation, Inc. <https://fsf.org/>
 *  Everyone is permitted to copy and distribute verbatim copies
 *  of this license document, but changing it is not allowed.
 */
using System;
using System.Drawing;
using Emgu.CV;
using Emgu.CV.CvEnum;

namespace UVtools.Core.PixelEditor
{
    public class PixelText : PixelOperation
    {
        public override PixelOperationType OperationType => PixelOperationType.Text;

        public FontFace Font { get; set; }

        public double FontScale { get; set; }
        public ushort Thickness { get; set; }
        public string Text { get; set; }
        public bool Mirror { get; set; }

        public bool IsAdd { get; }

        public byte Color { get; }

        public Rectangle Rectangle { get; }

        public PixelText(){}

        public PixelText(uint layerIndex, Point location, LineType lineType, FontFace font, double fontScale, ushort thickness, string text, bool mirror, bool isAdd) : base(layerIndex, location, lineType)
        {
            Font = font;
            FontScale = fontScale;
            Thickness = thickness;
            Text = text;
            Mirror = mirror;
            IsAdd = isAdd;

            Color = (byte) (isAdd ? 255 : 0);
            int baseLine = 0;
            Size = CvInvoke.GetTextSize(text, font, fontScale, thickness, ref baseLine);
            Rectangle = new Rectangle(location, Size);
        }

        
    }
}
