using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using QRCoder;

namespace XelsCCDesktopWalletApp.Common
{
  public  class QRCodeConverter
    {


        public ImageSource GenerateQRCode(string qrCodeData)
        {
            QRCodeGenerator qRCodeGenerator = new QRCodeGenerator();
           
            QRCodeData qRCodeData = qRCodeGenerator.CreateQrCode(qrCodeData, QRCodeGenerator.ECCLevel.H);
            QRCode qrCode = new QRCode(qRCodeData);
            Bitmap qrCodeImage = qrCode.GetGraphic(20);

           return BitmapToImageSource(qrCodeImage);

        }

        private ImageSource BitmapToImageSource(Bitmap bitmap)
        {
            using (MemoryStream memory = new MemoryStream())
            {
                bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Bmp);
                memory.Position = 0;
                BitmapImage bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memory;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();

                return bitmapImage;
            }
        }
    }
}
