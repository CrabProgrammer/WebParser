using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace WebApp
{
    public class Image
    {
        public string imageUrl { get; set; }
        public string imageName { get; set; }
        public string imageAlt { get; set; }
        public long imageSize { get; set; }

        public Image()
        {
            imageUrl = "";
            imageName = "";
            imageAlt = "";
            imageSize = 0;
        }
        public Image(string url, string name, string alt)
        {
            imageUrl = url;
            imageName = name;
            imageAlt = alt;
            imageSize = 0;

        }
        public Image(string url, string name)
        {
            imageUrl = url;
            imageName = name;
            imageAlt = "";
            imageSize = 0;

        }
    }
}
