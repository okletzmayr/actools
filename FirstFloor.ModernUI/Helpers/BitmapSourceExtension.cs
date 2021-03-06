﻿using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JetBrains.Annotations;

namespace FirstFloor.ModernUI.Helpers {
    public static class BitmapSourceExtension {
        public static BitmapFrame Resize(this BitmapSource bitmap, int width, int height, BitmapScalingMode scalingMode = BitmapScalingMode.HighQuality) {
            var group = new DrawingGroup();
            RenderOptions.SetBitmapScalingMode(group, scalingMode);
            group.Children.Add(new ImageDrawing(bitmap, new Rect(0, 0, width, height)));
            var targetVisual = new DrawingVisual();
            var targetContext = targetVisual.RenderOpen();
            targetContext.DrawDrawing(group);
            var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Default);
            targetContext.Close();
            target.Render(targetVisual);
            var targetFrame = BitmapFrame.Create(target);
            return targetFrame;
        }

        public static void SaveAsPng([NotNull] this BitmapSource bitmap, [NotNull] string filename) {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var s = File.Create(filename)) {
                encoder.Save(s);
            }
        }

        public static byte[] ToBytes([NotNull] this BitmapSource bitmap) {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var s = new MemoryStream()) {
                encoder.Save(s);
                return s.ToArray();
            }
        }
    }
}
