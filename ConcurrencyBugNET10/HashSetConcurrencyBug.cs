using System;
using System.Collections.Generic;
using System.Text;

namespace ConcurrencyBugNET10 {
    /// <summary>
    /// HashSet の並行アクセスによる破損を再現
    /// Dictionary と同様に読み取りスレッドが無限ループに陥る
    /// </summary>
    internal class HashSetConcurrencyBug {
        static HashSet<int> set = new HashSet<int>();
        static volatile bool writing = true;

        public static void Run() {
            Console.WriteLine("HashSet: 読み取りスレッドが無限ループに陥る可能性あり");

            // 読み取りスレッド
            var readers = new Thread[Environment.ProcessorCount];
            for (int i = 0; i < readers.Length; i++) {
                readers[i] = new Thread(Read);
                readers[i].Start();
            }

            // 書き込みスレッド
            var writers = new Thread[Environment.ProcessorCount];
            for (int i = 0; i < writers.Length; i++) {
                int id = i;
                writers[i] = new Thread(() => Add(id));
                writers[i].Start();
            }

            foreach (var t in writers) t.Join();
            writing = false;
            foreach (var t in readers) t.Join();
            Console.WriteLine("完了");
        }

        static void Add(int id) {
            int baseValue = id * 1000000;
            for (int i = 0; i < 1000000; i++) {
                try { set.Add(baseValue + i); } catch { }
            }
        }

        static void Read() {
            int value = 0;
            while (writing) {
                set.Contains(value++);
            }
        }
    }
}
