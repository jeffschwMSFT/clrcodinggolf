using CCMEngine;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClrCodingGolf.Analysis
{
    public static class CodeAnalysis
    {
        public static CodeMetrics Analyze(string text, bool injectIntrigue = false)
        {
            var metrics = new CodeMetrics() { Text = text };
            var timer = new Stopwatch();

            timer.Start();
            {
                // get raw bytes
                var bytes = ASCIIEncoding.ASCII.GetBytes(text);

                // raw analysis
                Raw(bytes, ref metrics);

                // ccm analysis
                CCM(bytes, ref metrics);

                // crazy character analysis
                if (injectIntrigue) Characters(bytes, ref metrics);
            }
            timer.Stop();

            metrics.Duration = timer.ElapsedMilliseconds;

            return metrics;
        }

        #region private
        private static bool Characters(byte[] bytes, ref CodeMetrics metrics)
        {
            // this is to inject some intrigue into the analysis
            for (int i = 0; i < bytes.Length; i++)
            {
                // Q, V - Z
                if (bytes[i] == 81 || (bytes[i] >= 86 && bytes[i] <= 90)) metrics.Characters -= 2f;
                // q, v - z
                else if (bytes[i] == 113 || (bytes[i] >= 118 && bytes[i] <= 122)) metrics.Characters -= 1f;
                // 0 - 9
                else if (bytes[i] >= 48 && bytes[i] <= 57) metrics.Characters += 0.75f;
                // A - Z
                else if (bytes[i] >= 65 && bytes[i] <= 90) metrics.Characters += 0.25f;
                // a - z
                else if (bytes[i] >= 97 && bytes[i] <= 122) metrics.Characters += 1f;
                // space
                else if (bytes[i] == 32) metrics.Characters += 1f;
            }

            return true;
        }

        private static bool Raw(byte[] bytes, ref CodeMetrics metrics)
        {
            // raw byte count
            metrics.Bytes = bytes.Length;

            // count newlines
            for (int i = 0; i < bytes.Length; i++)
                if (bytes[i] == '\n') metrics.Lines++;

            return true;
        }

        private static bool CCM(byte[] bytes, ref CodeMetrics metrics)
        {
            // create container for the metrics
            var listenter = new SortedListener(
                numMetrics: 30,
                ignores: new List<string>(),
                threshold: 0
            );

            // analyze
            using (var stream = new MemoryStream(bytes))
            {
                var reader = new StreamReader(stream);

                // analyze
                try
                {
                    var analyzer = new FileAnalyzer(
                        filestream: reader,
                        listenter,
                        context: null,
                        suppressMethodSignatures: false,
                        filename: "foo.cs"
                    );

                    analyzer.Analyze();

                    // consider the metrics in listener
                    foreach (var m in listenter.Metrics)
                    {
                        System.Diagnostics.Debug.WriteLine($"{m.Unit} {m.CCM} {m}");
                        metrics.CCM += m.CCM;
                        metrics.Methods++;
                    }
                }
                catch (Exception e)
                {
                    metrics.CCM = Single.MaxValue;
                    return false;
                }
            }

            return true;
        }
        #endregion
    }
}
