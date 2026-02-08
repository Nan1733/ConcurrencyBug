using System;
using System.Collections.Generic;
using System.Threading;

namespace ConcurrencyBug
{
    /// <summary>
    /// Dictionary の並行アクセスによる破損を再現
    /// 書き込みスレッドが内部構造を破損させ、読み取りスレッドが無限ループに陥る
    /// </summary>
    internal class DictionaryConcurrencyBug
    {
        static Dictionary<int, int> dict = new Dictionary<int, int>();
        static volatile bool writing = true;

        public static void Run()
        {
            Console.WriteLine("Dictionary: 読み取りスレッドが無限ループに陥る可能性あり");

            // 読み取りスレッド（無限ループに陥る側）
            var readers = new Thread[Environment.ProcessorCount];
            for (int i = 0; i < readers.Length; i++)
            {
                readers[i] = new Thread(Read);
                readers[i].Start();
            }

            // 書き込みスレッド（内部構造を破損させる側）
            var writers = new Thread[Environment.ProcessorCount];
            for (int i = 0; i < writers.Length; i++)
            {
                int id = i;
                writers[i] = new Thread(() => Write(id));
                writers[i].Start();
            }

            foreach (var t in writers) t.Join();
            writing = false;
            foreach (var t in readers) t.Join();
            Console.WriteLine("完了");
        }

        static void Write(int id)
        {
            // 書き込みにより内部配列のリサイズが発生し、構造が破損する
            int baseKey = id * 1000000;
            for (int i = 0; i < 1000000; i++)
            {
                try { dict[baseKey + i] = i; } catch { }
            }
        }

        static void Read()
        {
            // 破損した構造を読み取ると、循環参照により無限ループに陥る
            int key = 0;
            while (writing)
            {
                dict.ContainsKey(key++);
            }
        }
    }
}
