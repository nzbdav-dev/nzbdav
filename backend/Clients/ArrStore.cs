namespace NzbWebDAV.Clients;

public class ArrStore<T>
{
    // create a trie for the series paths so that we can look up by prefix
    private TrieNode _trie;
    private readonly Func<CancellationToken, Task<List<T>>> _getAllItems;
    private readonly Func<T, string> _getPath;
    public ArrStore(Func<CancellationToken, Task<List<T>>> getAllItems,
        Func<T, string> getPath)
    {
        _getAllItems = getAllItems;
        _getPath = getPath;
        _trie = new();
    }

    public async Task<T?> FindItemForPathAsync(string path, CancellationToken ct = default)
    {
        var item = _trie.FindLongestPrefix(path);
        if (item is null)
        {
            await UpdateTrieAsync(ct);
            item = _trie.FindLongestPrefix(path);
        }
        return item;
    }

    private async Task UpdateTrieAsync(CancellationToken ct = default)
    {
        var allItems = await _getAllItems(ct);
        var trie = new TrieNode();
        foreach (var item in allItems)
        {
            if (!string.IsNullOrEmpty(_getPath(item)))
            {
                trie.Add(_getPath(item), item);
            }
        }
        _trie = trie;
    }

    private class TrieNode
    {
        private readonly Dictionary<char, TrieNode> _children = new();

        public T? Value { get; set; }

        public void Add(string path, T value)
        {
            var current = this;
            foreach (var c in path)
            {
                if (!current._children.ContainsKey(c))
                {
                    current._children[c] = new TrieNode();
                }
                current = current._children[c];
            }
            current.Value = value;
        }

        public T? FindLongestPrefix(string path)
        {
            var current = this;
            T? lastValue = current.Value; // Track the last valid value we found

            foreach (var c in path)
            {
                if (!current._children.ContainsKey(c)) break;

                current = current._children[c];

                // Update lastValue if this node has a value (represents a complete path)
                if (current.Value != null)
                {
                    lastValue = current.Value;
                }
            }

            return lastValue;
        }
    }
}
