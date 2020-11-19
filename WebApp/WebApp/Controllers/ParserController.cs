using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NLog;

namespace WebApp.Controllers
{
    [ApiController]
    [Route("Parser")]
    public class ParserController : ControllerBase
    {
        //создаём объект логгер из библиотеки NLog
        //конфигурация логгера находится в NLog.config
        private static Logger logger = LogManager.GetCurrentClassLogger();

        //REST GET запрос 
        [HttpGet]
        public IEnumerable<Parser> GetImages(string url, int threadCount, int imageCount)
        {
            StringBuilder htmlText = GetHtml(url); //скачиваем весь HTML код страницы
            List<Image> imageList = new List<Image>();
            int nameIndex; //позиция последнего / в строке url

            //вытаскиваем <img> из html кода
            string imgPattern = @"<(img)\b[^>]*>"; 
            List<string> imgStringList = new List<string>();
            Regex imgRegex = new Regex(imgPattern, RegexOptions.IgnoreCase);
            MatchCollection imgMatches = imgRegex.Matches(htmlText.ToString());
            for (int i = 0; i < imgMatches.Count; i++) 
                imgStringList.Add(imgMatches[i].Value);//добавляем все совпадения в список <img>


            //регулярное выражения для вытаскивания поля src тега IMG
            string srcPattern = "src=\"[^\"]*\"";
            Regex srcRegex = new Regex(srcPattern, RegexOptions.IgnoreCase);

            //регулярное выражения для вытаскивания поля alt тега IMG
            string altPattern = "alt=\"[^\"]*\"";
            Regex altRegex = new Regex(altPattern, RegexOptions.IgnoreCase);

            //переменная для ограничения итераций цикла. Увеличивается при условии что поле src было пустым 
            int checkImages=imageCount; 
            for (int i = 0; i < checkImages; i++)
            {
                Match srcMatch = srcRegex.Match(imgStringList[i]); //
                Match altMatch = altRegex.Match(imgStringList[i]);

                if (srcMatch.Value.Length <= 6) // если src=""
                {
                    checkImages++;
                    continue;
                }
                else
                {
                    string srcUrl = srcMatch.Value.Substring(5, srcMatch.Value.Length - 6);
                    nameIndex = srcUrl.LastIndexOf("/");
                    string imageName = srcUrl.Substring(nameIndex + 1, srcUrl.Length - nameIndex - 1);

                    string altImg = "";
                    if (altMatch.Value.Length > 6) //если alt не пустой
                    {
                        altImg = altMatch.Value.Substring(5, altMatch.Length - 6);
                    }
                    //добавляем изображения в итоговый список
                    imageList.Add(new Image(srcUrl, imageName, altImg));
                }
            }


            if (imageCount > imageList.Count)
                imageCount = imageList.Count;

            //считываем количество логических процессоров
            int processorCount = Environment.ProcessorCount;

            if (threadCount > processorCount)
                threadCount = processorCount;

            if (threadCount > imageCount)
                threadCount = imageCount;

            //список изображений для каждого потока
            List<Image> tmpImageList = new List<Image>();

            //расчитываем количество изображений на поток
            //оставшиеся изображения будут скачаны в последнем потоке
            int remainingImages = 0;
            int imagePerThread = imageCount / threadCount; 
            if (imageCount % threadCount > 0)
                remainingImages = imageCount % threadCount;

            List<Thread> threads = new List<Thread>();

            for (int i = 0; i < threadCount; i++)
            {
                //записываем в каждый поток соответсвующий список изображений 
                for (int j = i * imagePerThread; j < (i + 1) * imagePerThread; j++)
                {
                    tmpImageList.Add(new Image(imageList[j].imageUrl,
                        imageList[j].imageName, imageList[j].imageAlt));
                }
                if (i == threadCount - 1) //если последний поток
                {
                    //добавляем оставшиеся изображения
                    for (int j = (i + 1) * imagePerThread; j < imageCount; j++)
                    {
                        tmpImageList.Add(new Image(imageList[j].imageUrl,
                            imageList[j].imageName, imageList[j].imageAlt));
                    }
                }
                //создаём новый поток, передаём в него список изображений и добавляем в список потоков
                Thread newThread = new Thread(new ParameterizedThreadStart(DownloadImage));
                newThread.Start(tmpImageList);
                threads.Add(newThread);
                //специально не освобождаем (clear), а выделяем новую память, так как с этой 
                //областью памяти всё ещё работает поток
                tmpImageList = new List<Image>();
            }

            List<Parser> parserList = new List<Parser>(); //список объектов возвращаемых в JSON
            DateTime end = DateTime.Now.AddMinutes(1); //засекаем минуту после запуска потоков
            while (true)
            {
                //проверяем отработали ли потоки
                int alive = 0;
                for (int i = 0; i < threads.Count; i++) 
                {
                    if (threads[i].IsAlive)
                        alive++;
                }
                if (alive == 0) //если отработали
                {
                    for (int i = 0; i < imageCount; i++)
                    {
                        try //проверяем размер изображений
                        {
                            System.IO.FileInfo file = new System.IO.FileInfo("Images\\" + imageList[i].imageName);
                            imageList[i].imageSize = file.Length;
                        }
                        catch (Exception ex)
                        {
                            logger.Error("Checking file size error.\nExeption code:\t" + ex.Message + "\n");
                        }
                    }
                    foreach (Image img in imageList) //копируем список изображений, добавляя имя хоста
                    {
                        parserList.Add(new Parser { host = "localhost", image = img });
                    }
                    break;
                }
                if (DateTime.Now > end) //если минута истекла
                {
                    logger.Info("Downloading more then 1min.\n");
                    //JSON вернёт одну запись с данным заголовком:
                    parserList.Add(new Parser { host = "Downloading Time Error", image = null });
                    break;
                }
            }
            return parserList;
        }

        public static void DownloadImage(object obj)
        {
            //присваиваем передаваемый объект
            List<Image> imageList = obj as List<Image>;

            using (WebClient client = new WebClient())
            {
                for (int i = 0; i < imageList.Count; i++)
                {
                    try
                    {
                        //если вначале отстутствует https:
                        if (imageList[i].imageUrl[0] == '/' && imageList[i].imageUrl[1] == '/')
                            imageList[i].imageUrl = "https:" + imageList[i].imageUrl;
                        //пытаемся скачать изображение в каталог \Image\Filename
                        client.DownloadFile(imageList[i].imageUrl, "Images\\" + imageList[i].imageName);

                    }
                    catch (Exception ex)
                    {
                        logger.Error("Image downloading error.\nExeption code:\t" + ex.Message + "\n");
                    }
                }
            }
        }

        public static StringBuilder GetHtml(string url)
        {
            //считываем весь html код страницы
            StringBuilder sb = new StringBuilder();
            byte[] buf = new byte[8192];
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            Stream resStream = response.GetResponseStream();
            int count;
            try
            {
                do
                {
                    count = resStream.Read(buf, 0, buf.Length);
                    if (count != 0)
                    {
                        sb.Append(Encoding.Default.GetString(buf, 0, count));
                    }
                }
                while (count > 0);
            }
            catch (Exception ex)
            {
                logger.Error("HTML reading error.\nExeption code:\t" + ex);
            }

            return sb;
        }
    }
}
