using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

class Program
{
    static void Main()
    {
        Bitmap bmp = new Bitmap(32, 32);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            Brush bgBrush = Brushes.Teal;
            Pen borderPen = Pens.DarkSlateGray;
            
            g.FillEllipse(bgBrush, 2, 2, 28, 28);
            g.DrawEllipse(borderPen, 2, 2, 28, 28);
            g.FillEllipse(Brushes.White, 12, 12, 8, 8);
        }

        // Save as ICO
        using (FileStream fs = new FileStream("JigglrIcon.ico", FileMode.Create))
        {
            Icon icon = Icon.FromHandle(bmp.GetHicon());
            icon.Save(fs);
        }
    }
}
