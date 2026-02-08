# 非スレッドセーフコレクションの並行アクセスによる破損

## 概要

.NET の `Dictionary<TKey, TValue>`、`List<T>`、`HashSet<T>` などの標準コレクションはスレッドセーフではありません。複数のスレッドから同時にこれらのコレクションを変更すると、内部状態が破損し、以下のような深刻な問題が発生する可能性があります：

- **CPU 使用率 100%**（無限ループに陥る）
- **データ消失**
- **予期しない例外**
- **デッドロック**

## Dictionary における無限ループの原理

### 内部構造

`Dictionary<TKey, TValue>` は **ハッシュテーブル** として実装されています。主要なコンポーネントは：

- **buckets**: ハッシュ値からエントリへの参照を保持する配列
- **entries**: 実際のキーと値を格納する配列
- **next**: 同じバケットに属するエントリをリンクリストとして連結するためのインデックス

```mermaid
graph TD
    subgraph "Dictionary 内部構造"
        B[buckets配列]
        E[entries配列]
        
        B -->|"bucket[hash % size]"| E
        E -->|"next"| E2[次のエントリ]
        E2 -->|"next"| E3[次のエントリ]
        E3 -->|"-1 = 終端"| END[リスト終了]
    end
```

### 正常な Resize 処理

コレクションの容量が不足すると、内部配列の **Resize（リサイズ）** が発生します：

```mermaid
sequenceDiagram
    participant T as スレッド
    participant D as Dictionary
    participant B as buckets
    participant E as entries
    
    T->>D: Add(key, value)
    D->>D: 容量チェック
    alt 容量不足
        D->>E: 新しい entries 配列を確保
        D->>E: 全エントリをコピー
        D->>B: 新しい buckets 配列を確保
        D->>B: 全エントリを再ハッシュ
        D->>D: 参照を新配列に切り替え
    end
    D->>E: 新エントリを追加
```

### 並行アクセス時の破損

複数スレッドが同時に操作すると、Resize 処理の途中で他のスレッドが介入し、データ構造が破損します：

```mermaid
sequenceDiagram
    participant T1 as スレッド1
    participant T2 as スレッド2
    participant D as Dictionary
    participant B as buckets
    participant E as entries
    
    T1->>D: Add(key1, value1)
    T1->>D: Resize開始
    T1->>E: 新entries確保
    
    Note over T1,T2: スレッド切り替え発生
    
    T2->>D: Add(key2, value2)
    T2->>E: 古いentriesに追加 ??
    T2->>B: nextリンクを更新 ??
    
    Note over T1,T2: スレッド切り替え発生
    
    T1->>B: 新bucketsで再ハッシュ
    T1->>D: 新配列に切り替え
    
    Note over D: 循環参照が発生！
```

### 循環参照による無限ループ

`next` フィールドの更新が競合すると、リンクリストに **循環参照** が形成されます：

```mermaid
graph LR
    subgraph "正常な状態"
        A1[Entry A] -->|next| B1[Entry B] -->|next| C1[Entry C] -->|next = -1| END1[終端]
    end
```

```mermaid
graph LR
    subgraph "破損した状態（循環参照）"
        A2[Entry A] -->|next| B2[Entry B]
        B2 -->|next| C2[Entry C]
        C2 -->|next| A2
    end
```

循環参照が発生すると、キーの検索時にリンクリストを無限に辿り続け、**CPU 使用率が 100% に張り付きます**。

## List における問題

`List<T>` では、内部配列の拡張時に以下の問題が発生します：

```mermaid
sequenceDiagram
    participant T1 as スレッド1
    participant T2 as スレッド2
    participant L as List
    participant A as 内部配列
    
    T1->>L: Add(item1)
    T1->>L: _size読み取り (例: 10)
    
    Note over T1,T2: スレッド切り替え
    
    T2->>L: Add(item2)
    T2->>L: _size読み取り (同じ10)
    T2->>A: array[10] = item2
    T2->>L: _size = 11
    
    Note over T1,T2: スレッド切り替え
    
    T1->>A: array[10] = item1 ?? 上書き！
    T1->>L: _size = 11 ?? 12であるべき
    
    Note over L: item2 が消失！
```

## HashSet / Queue における問題

これらも同様の内部構造を持つため、同様の問題が発生します：

| コレクション | 内部構造 | 主な問題 |
|-------------|---------|---------|
| `Dictionary<K,V>` | ハッシュテーブル + リンクリスト | 循環参照による無限ループ |
| `HashSet<T>` | ハッシュテーブル + リンクリスト | 循環参照による無限ループ |
| `List<T>` | 動的配列 | データ消失、IndexOutOfRangeException |
| `Queue<T>` | 循環配列 | データ消失、無限ループ |
| `Stack<T>` | 動的配列 | データ消失 |

## .NET バージョンごとの対応

### .NET 9 での検出機能追加

.NET 9 から、非スレッドセーフコレクションへの並行アクセスを**検出する機能**が追加されました。これにより、破損が発生する前に `InvalidOperationException` がスローされるようになりました。

**参照情報:**
- [Breaking change: .NET 9 - Non-concurrent collections don't throw on concurrent access](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/9.0/concurrency-check)
- [GitHub Issue #100337](https://github.com/dotnet/runtime/issues/100337)
- [GitHub PR #103419](https://github.com/dotnet/runtime/pull/103419)

### エラーメッセージ

.NET 9 以降では、並行アクセスが検出されると以下のようなメッセージが表示されます：

```
System.InvalidOperationException: Operations that change non-concurrent collections must have exclusive access. A concurrent update was performed on this collection and corrupted its state. The collection's state is no longer correct.
```

### バージョン別対応表

| .NET バージョン | 動作 |
|----------------|------|
| .NET Framework 全バージョン | 検出なし。破損や無限ループが発生 |
| .NET Core 1.0 - 3.1 | 検出なし。破損や無限ループが発生 |
| .NET 5 - 8 | 検出なし。破損や無限ループが発生 |
| .NET 9 以降 | **並行アクセスを検出し例外をスロー** |

## 解決方法

### 1. スレッドセーフなコレクションを使用する

```csharp
// Dictionary の代わりに ConcurrentDictionary を使用
using System.Collections.Concurrent;

var dict = new ConcurrentDictionary<int, string>();
```

| 非スレッドセーフ | スレッドセーフ代替 |
|-----------------|-------------------|
| `Dictionary<K,V>` | `ConcurrentDictionary<K,V>` |
| `Queue<T>` | `ConcurrentQueue<T>` |
| `Stack<T>` | `ConcurrentStack<T>` |
| `List<T>` | `ConcurrentBag<T>` または `lock` |
| `HashSet<T>` | `ConcurrentDictionary<T,byte>` または `lock` |

### 2. ロックを使用する

```csharp
private static readonly object lockObj = new object();
private static Dictionary<int, string> dict = new Dictionary<int, string>();

// 書き込み
lock (lockObj)
{
    dict[key] = value;
}

// 読み取り
lock (lockObj)
{
    return dict[key];
}
```

### 3. ReaderWriterLockSlim を使用する

読み取りが多い場合に有効です：

```csharp
private static ReaderWriterLockSlim rwLock = new ReaderWriterLockSlim();
private static Dictionary<int, string> dict = new Dictionary<int, string>();

// 書き込み
rwLock.EnterWriteLock();
try
{
    dict[key] = value;
}
finally
{
    rwLock.ExitWriteLock();
}

// 読み取り
rwLock.EnterReadLock();
try
{
    return dict[key];
}
finally
{
    rwLock.ExitReadLock();
}
```

## 参考資料

- [Microsoft Docs: Thread-Safe Collections](https://learn.microsoft.com/en-us/dotnet/standard/collections/thread-safe/)
- [Dictionary&lt;TKey,TValue&gt; Class - Thread Safety](https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.dictionary-2#thread-safety)
- [.NET 9 Breaking Change: Concurrency Check](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/9.0/concurrency-check)
- [GitHub: dotnet/runtime Issue #100337](https://github.com/dotnet/runtime/issues/100337)
- [GitHub: dotnet/runtime PR #103419](https://github.com/dotnet/runtime/pull/103419)
