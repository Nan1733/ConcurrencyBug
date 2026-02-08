namespace ConcurrencyBugNET8 {
    /// <summary>
    /// 非スレッドセーフコレクションの並行アクセスバグ再現プログラム
    /// </summary>
    internal class Program {
        static void Main(string[] args) {
            Console.WriteLine("並行アクセスバグ再現");
            Console.WriteLine("1: Dictionary");
            Console.WriteLine("2: HashSet");
            Console.Write("番号を入力: ");

            switch (Console.ReadLine()) {
                case "1": DictionaryConcurrencyBug.Run(); break;
                case "2": HashSetConcurrencyBug.Run(); break;
            }
        }
    }
}
