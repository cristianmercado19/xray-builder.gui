﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Entity.Design.PluralizationServices;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;
using HtmlAgilityPack;

namespace XRayBuilderGUI
{
    public static class Functions
    {
        public static string ReadFromFile(string file)
        {
            using (var streamReader = new StreamReader(file, Encoding.UTF8))
                return streamReader.ReadToEnd();
        }

        //http://www.levibotelho.com/development/c-remove-diacritics-accents-from-a-string/
        public static string RemoveDiacritics(this string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            text = text.Normalize(NormalizationForm.FormD);
            var chars = text.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) !=
                UnicodeCategory.NonSpacingMark).ToArray();

            return new string(chars).Normalize(NormalizationForm.FormC);
        }

        // TODO: Clean this up more cause it still sucks
        public static string Clean(this string str)
        {
            (string[] searches, string replace)[] replacements =
            {
                (new[] {"&#169;", "&amp;#169;", "&#174;", "&amp;#174;", "&mdash;", @"</?[a-z]+>" }, ""),
                (new[] { "“", "”", "\"" }, "'"),
                (new[] { "&#133;", "&amp;#133;", @" \. \. \." }, "…"),
                (new[] { " - ", "--" }, "—"),
                (new[] { @"\t|\n|\r|•", @"\s+"}, " "),
                (new[] { @"\. …$"}, "."),
                (new[] {"@", "#", @"\$", "%", "_", }, "")
            };
            foreach (var (s, r) in replacements)
            {
                str = Regex.Replace(str, $"({string.Join("|", s)})", r, RegexOptions.Multiline);
            }
            return str.Trim();
        }

        public static string ImageToBase64(Image image, ImageFormat format)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                image.Save(ms, format);
                return Convert.ToBase64String(ms.ToArray());
            }
        }

        public static Bitmap Base64ToImage(string base64String)
        {
            byte[] imageBytes = Convert.FromBase64String(base64String);
            using (MemoryStream ms = new MemoryStream(imageBytes, 0, imageBytes.Length))
                return new Bitmap(ms);
        }

        public static Bitmap MakeGrayscale3(Bitmap original)
        {
            //create a blank bitmap the same size as original
            Bitmap newBitmap = new Bitmap(original.Width, original.Height);

            //get a graphics object from the new image
            Graphics g = Graphics.FromImage(newBitmap);

            //create the grayscale ColorMatrix
            ColorMatrix colorMatrix = new ColorMatrix(
                new[]
                {
                    new [] {.3f, .3f, .3f, 0, 0},
                    new [] {.59f, .59f, .59f, 0, 0},
                    new [] {.11f, .11f, .11f, 0, 0},
                    new [] {0f, 0f, 0f, 1f, 0f},
                    new [] {0f, 0f, 0f, 0f, 1f}
                });

            //create some image attributes
            ImageAttributes attributes = new ImageAttributes();

            //set the color matrix attribute
            attributes.SetColorMatrix(colorMatrix);

            //draw the original image on the new image
            //using the grayscale color matrix
            g.DrawImage(original, new Rectangle(0, 0, original.Width, original.Height),
                0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);

            //dispose the Graphics object
            g.Dispose();
            return newBitmap;
        }

        public static string GetBookOutputDirectory(string author, string title, bool create)
        {
            var newAuthor = RemoveInvalidFileChars(author);
            var newTitle = RemoveInvalidFileChars(title);
            var path = Path.Combine(Properties.Settings.Default.outDir, $"{newAuthor}\\{newTitle}");
            if (create)
                Directory.CreateDirectory(path);
            return path;
        }

        public static string RemoveInvalidFileChars(string filename)
        {
            char[] fileChars = Path.GetInvalidFileNameChars();
            return new string(filename.Where(x => !fileChars.Contains(x)).ToArray());
        }

        public static bool ExtrasExist(string location, string asin)
        {
            if (File.Exists(location + $"\\AuthorProfile.profile.{asin}.asc") && File.Exists(location + $"\\EndActions.data.{asin}.asc"))
                return true;
            return false;
        }

        //public static string GetTempDirectory()
        //{
        //    string path;
        //    do
        //    {
        //        path = Path.Combine(Properties.Settings.Default.tmpDir, Path.GetRandomFileName());
        //    } while (Directory.Exists(path));
        //    Directory.CreateDirectory(path);
        //    return path;
        //}

        public static string TimeStamp()
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            var time = string.Format("{0:HH:mm:ss}", DateTime.Now);
            var date = string.Format("{0:dd/MM/yyyy}", DateTime.Now);
            return $"Running X-Ray Builder GUI v{version}. Log started on {date} at {time}.\r\n";
        }

        public static string GetPageCount(string rawML, BookInfo bookInfo)
        {
            string output;
            double lineCount = 0;
            if (!File.Exists(rawML) || bookInfo == null)
            {
                output = "Error: RawML could not be found, aborting.\r\nPath: " + rawML;
                return output;
            }
            HtmlDocument bookDoc = new HtmlDocument { OptionAutoCloseOnEnd = true };
            bookDoc.Load(rawML, Encoding.UTF8);
            var booklineNodes = bookDoc.DocumentNode.SelectNodes("//p") ?? bookDoc.DocumentNode.SelectNodes("//div");
            if (booklineNodes == null)
            {
                output = "An error occurred while estimating page count!";
                return output;
            }
            foreach (HtmlNode line in booklineNodes)
            {
                var lineLength = line.InnerText.Length + 1;
                if (lineLength < 70)
                {
                    lineCount++;
                    continue;
                }
                lineCount += Math.Ceiling((double)lineLength / 70);
            }
            var pageCount = Convert.ToInt32(Math.Ceiling(lineCount / 31));
            if (pageCount == 0)
            {
                output = "An error occurred while estimating page count!";
                return output;
            }
            double minutes = pageCount * 1.2890625;
            TimeSpan span = TimeSpan.FromMinutes(minutes);
            bookInfo.PagesInBook = pageCount;
            bookInfo.ReadingHours = span.Hours;
            bookInfo.ReadingMinutes = span.Minutes;
            output = $"Typical time to read: {span.Hours} hours and {span.Minutes} minutes ({bookInfo.PagesInBook} pages)";
            return output;
        }

        public static void RunNotepad(string filename)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "notepad",
                Arguments = filename,
                UseShellExecute = false
            };
            try
            {
                using (Process process = Process.Start(startInfo))
                {
                    process?.WaitForExit();
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Error trying to launch notepad.", ex);
            }
        }

        //http://stackoverflow.com/questions/4123590/serialize-an-object-to-xml
        public static string Serialize<T>(T value)
        {
            if (value == null)
                return string.Empty;

            var xmlserializer = new XmlSerializer(typeof(T));
            var stringWriter = new StringWriter();
            using (var writer = XmlWriter.Create(stringWriter))
            {
                xmlserializer.Serialize(writer, value);
                return stringWriter.ToString();
            }
        }

        public static void Save<T>(T output, string fileName) where T : class
        {
            using (var writer = new StreamWriter(fileName, false, Encoding.UTF8))
            {
                var serializer = new XmlSerializer(typeof(T));
                serializer.Serialize(writer, output);
                writer.Flush();
            }
        }

        //http://stackoverflow.com/questions/14562415/xml-deserialization-generic-method
        public static List<T> DeserializeList<T>(string filePath)
        {
            var itemList = new List<T>();

            if (!File.Exists(filePath)) return itemList;

            var serializer = new XmlSerializer(typeof(List<T>));
            TextReader reader = new StreamReader(filePath, Encoding.UTF8);
            try
            {
                itemList = (List<T>)serializer.Deserialize(reader);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"Error processing XML file: {ex.Message}"
                                               + "\r\nIf the error contains a (#, #), the first number is the line the error occurred on.", ex);
            }
            reader.Close();

            return itemList;
        }

        /// <summary>
        /// Fix author name if in last, first format or if multiple authors present (returns first author)
        /// </summary>
        public static string FixAuthor(string author)
        {
            if (author == null) return null;
            if (author.Contains(';'))
                author = author.Split(';')[0];
            if (author.Contains(','))
            {
                string[] parts = author.Split(',');
                author = parts[1].Trim() + " " + parts[0].Trim();
            }
            return author;
        }

        public static string ExpandUnicode(string input)
        {
            StringBuilder output = new StringBuilder(input.Length);
            for (int i = 0; i < input.Length; i++)
            {
                if (input[i] > 127)
                {
                    byte[] uniBytes = Encoding.Unicode.GetBytes(input.Substring(i, 1));
                    output.AppendFormat(@"\u{0:X2}{1:X2}", uniBytes[1], uniBytes[0]);
                }
                else
                    output.Append(input[i]);
            }
            return output.ToString();
        }

        // Shamelessly stolen from http://www.mobileread.com/forums/showthread.php?t=185565
        public static byte[] CheckBytes(byte[] bytesToCheck)
        {
            if (bytesToCheck == null) return null;
            byte[] buffer = (byte[])bytesToCheck.Clone();
            if (BitConverter.IsLittleEndian)
                Array.Reverse(buffer);
            return buffer;
        }

        public static bool CleanUp(string folderPath)
        {
            if (!Directory.Exists(folderPath))
                return false;

            string[] files = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories);
            string[] dirs = Directory.GetDirectories(folderPath, "*", SearchOption.AllDirectories);

            foreach (string file in files)
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }

            foreach (string dir in dirs)
            {
                CleanUp(dir);
            }
            Thread.Sleep(1);
            Directory.Delete(folderPath, false);
            return true;
        }

        /// <summary>
        /// Process GUID. If in decimal form, convert to hex.
        /// </summary>
        public static string ConvertGuid(string guid)
        {
            if (Regex.IsMatch(guid, "/[a-zA-Z]/", RegexOptions.Compiled))
                guid = guid.ToUpper();
            else
            {
                long.TryParse(guid, out var guidDec);
                guid = guidDec.ToString("X");
            }

            if (guid == "0")
                throw new ArgumentException("An error occurred while converting the GUID.");

            return guid;
        }

        public static bool ValidateFilename(string author, string title)
        {
            var newAuthor = RemoveInvalidFileChars(author);
            var newTitle = RemoveInvalidFileChars(title);
            return author.Equals(newAuthor) && title.Equals(newTitle);
        }

        public static long UnixTimestampSeconds()
        {
            return (long) DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        public static long UnixTimestampMilliseconds()
        {
            return (long) DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }

        public static string Pluralize(FormattableString formattable) => formattable.ToString(new PluralFormatProvider());

        // https://stackoverflow.com/questions/311165/how-do-you-convert-a-byte-array-to-a-hexadecimal-string-and-vice-versa/14333437#14333437
        /// <summary>
        /// Convert a byte array to hex string quickly
        /// </summary>
        public static string ByteToHexString(byte[] bytes)
        {
            var c = new char[bytes.Length * 2];
            int b;
            for (int i = 0; i < bytes.Length; i++)
            {
                b = bytes[i] >> 4;
                c[i * 2] = (char)(55 + b + (((b - 10) >> 31) & -7));
                b = bytes[i] & 0xF;
                c[i * 2 + 1] = (char)(55 + b + (((b - 10) >> 31) & -7));
            }

            return new string(c);
        }

        public static byte[] Sha1(byte[] bytes)
        {
            using (var sha1 = new SHA1Managed())
                return sha1.ComputeHash(bytes);
        }
    }

    internal static class NativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }

    public static partial class ExtensionMethods
    {
        //http://stackoverflow.com/questions/166855/c-sharp-preg-replace
        public static string PregReplace(this string input, string[] pattern, string[] replacements)
        {
            if (replacements.Length != pattern.Length)
                throw new ArgumentException("Replacement and pattern arrays must be balanced");

            for (var i = 0; i < pattern.Length; i++)
            {
                input = Regex.Replace(input, pattern[i], replacements[i]);
            }
            return input;
        }

        //http://stackoverflow.com/questions/444798/case-insensitive-containsstring
        public static bool Contains(this string source, string toCheck, StringComparison comp)
            => source?.IndexOf(toCheck, comp) >= 0;

        public static bool ContainsIgnorecase(this string source, string toCheck)
            => source.Contains(toCheck, StringComparison.OrdinalIgnoreCase);

        public static void AddNotNull<T>(this IList<T> list, T value)
        {
            if (value != null) list.Add(value);
        }

        public static void AddNotNull<T>(this ConcurrentBag<T> list, T value)
        {
            if (value != null) list.Add(value);
        }

        public static string Plural(this string value, int count)
        {
            return count == 1
                ? value
                : PluralizationService
                    .CreateService(new CultureInfo("en-US"))
                    .Pluralize(value);
        }

        public static int? TryParseInt(this string s, NumberStyles style, IFormatProvider provider)
        {
            return int.TryParse(s, style, provider, out var result)
                ? (int?) result
                : null;
        }

        public static Match MatchOrNull(this Regex regex, string input)
        {
            if (input == null)
                return null;

            var match = regex.Match(input);
            return match.Success ? match : null;
        }

        public static TValue GetOrDefault<TKey, TValue>(this Dictionary<TKey, TValue> dic, TKey key)
            => dic.TryGetValue(key, out var val) ? val : default;

        public static TValue GetOrThrow<TKey, TValue>(this Dictionary<TKey, TValue> dic, TKey key)
        {
            if (dic.TryGetValue(key, out var val))
                return val;

            throw new ArgumentException("Key not found", nameof(key));
        }

        public static void Deconstruct<T1, T2>(this KeyValuePair<T1, T2> tuple, out T1 key, out T2 value)
        {
            key = tuple.Key;
            value = tuple.Value;
        }

        /// <summary>
        /// Read <paramref name="count"/> bytes from <paramref name="stream"/> after seeking to <paramref name="offset"/> from <paramref name="origin"/>
        /// </summary>
        public static byte[] ReadBytes(this Stream stream, int offset, int count, SeekOrigin origin)
        {
            stream.Seek(offset, origin);
            return stream.ReadBytes(count);
        }

        public static byte[] ReadBytes(this Stream stream, int count)
        {
            var buffer = new byte[count];

            int offset = 0;
            while (offset < count)
            {
                var read = stream.Read(buffer, offset, count - offset);
                if (read == 0)
                    throw new EndOfStreamException();

                offset += read;
            }

            return buffer;
        }

        public static byte[] ReadToEnd(this Stream stream)
        {
            var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
    }

    public class PluralFormatProvider : IFormatProvider, ICustomFormatter
    {
        public object GetFormat(Type formatType) => this;

        public string Format(string format, object arg, IFormatProvider formatProvider)
            => arg + " " + format.Plural((int) arg);
    }
}