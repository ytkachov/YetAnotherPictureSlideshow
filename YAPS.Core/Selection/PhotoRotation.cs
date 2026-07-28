using System;
using System.Collections.Generic;

namespace Yaps.Core.Selection;

/// <summary>
/// Fair, folder-batched rotation over the photo library.
///
/// The slideshow shows photos in mini-batches from one folder at a time —
/// that's a deliberate property (consecutive shots from the same trip read as
/// a story, not as noise). The naive way to do it, picking a random folder
/// each time, is what made the rotation unfair: a folder of five photos was
/// picked as often as a folder of three thousand, so each of those five got
/// hundreds of times more screen time.
///
/// This class keeps the batching and removes the bias by dealing from two
/// decks instead of rolling dice:
///
/// <list type="bullet">
/// <item><b>Folder deck</b> — every folder appears in it once per batch it
/// needs to be fully covered (<c>ceil(photos / batchSize)</c>), then the whole
/// deck is shuffled. A folder of 3000 photos comes up 300 times per pass, a
/// folder of 5 comes up once.</item>
/// <item><b>Photo deck</b> — each folder has its own shuffled order with a
/// cursor; a visit takes the next <c>batchSize</c> entries (or whatever is
/// left). No photo comes back until its folder's deck is exhausted.</item>
/// </list>
///
/// The two stay in lockstep — the folder deck grants a folder exactly as many
/// visits as its photo deck has batches — so over one pass <b>every photo in
/// the library is shown exactly once</b>, no matter how the photos are spread
/// across folders.
///
/// A photo deck is (re)built least-shown-first, with a random tiebreak inside
/// equal counts. Within a pass that's just a shuffle (all counts are equal by
/// then); across restarts it's what repairs an existing imbalance — photos the
/// registry never saw on screen go to the front of the queue.
///
/// Not thread-safe: the caller (LocalImages) already serialises selection
/// under its own lock, and a second lock here would buy nothing.
/// </summary>
public sealed class PhotoRotation
{
    private readonly IReadOnlyDictionary<string, int[]> _photosByFolder;
    private readonly Func<int, int> _showCountOf;
    private readonly int _batchSize;
    private readonly Dictionary<string, Deck> _decks;

    private string[] _folderDeck = Array.Empty<string>();
    private int _folderCursor;

    /// <param name="photosByFolder">
    /// Photo ids grouped by folder. Ids are opaque here — they are indices
    /// into the caller's photo array — so this class stays free of any
    /// file-system knowledge.
    /// </param>
    /// <param name="showCountOf">
    /// How often a photo has been shown historically (from the persisted show
    /// registry). Called only while a folder's deck is being built.
    /// </param>
    /// <param name="photosPerFolder">Batch size; how many photos one folder visit yields.</param>
    public PhotoRotation(IReadOnlyDictionary<string, int[]> photosByFolder, Func<int, int> showCountOf, int photosPerFolder)
    {
        _photosByFolder = photosByFolder ?? throw new ArgumentNullException(nameof(photosByFolder));
        _showCountOf = showCountOf ?? throw new ArgumentNullException(nameof(showCountOf));
        _batchSize = Math.Max(1, photosPerFolder);
        _decks = new Dictionary<string, Deck>(photosByFolder.Count, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Next mini-batch of photo ids, all from one folder. Empty only when the
    /// library itself is empty.
    /// </summary>
    public int[] NextBatch()
    {
        string? folder = NextFolder();
        if (folder is null)
            return Array.Empty<int>();

        if (!_decks.TryGetValue(folder, out var deck) || deck.Cursor >= deck.Order.Length)
        {
            deck = new Deck(BuildPhotoDeck(folder));
            _decks[folder] = deck;
        }

        int take = Math.Min(_batchSize, deck.Order.Length - deck.Cursor);
        var batch = new int[take];
        Array.Copy(deck.Order, deck.Cursor, batch, 0, take);
        deck.Cursor += take;
        return batch;
    }

    private string? NextFolder()
    {
        if (_folderCursor >= _folderDeck.Length)
        {
            _folderDeck = BuildFolderDeck();
            _folderCursor = 0;
        }

        return _folderDeck.Length == 0 ? null : _folderDeck[_folderCursor++];
    }

    // One entry per batch the folder needs, so folder visits are proportional
    // to folder size — this is the actual fix for the old "random folder"
    // bias. Shuffled so the visits are spread out rather than clustered.
    private string[] BuildFolderDeck()
    {
        int total = 0;
        foreach (var pair in _photosByFolder)
            total += Visits(pair.Value.Length);

        var deck = new string[total];
        int at = 0;
        foreach (var pair in _photosByFolder)
        {
            int visits = Visits(pair.Value.Length);
            for (int i = 0; i < visits; i++)
                deck[at++] = pair.Key;
        }

        Shuffle(deck);
        return deck;
    }

    private int Visits(int photoCount) => photoCount <= 0 ? 0 : (photoCount + _batchSize - 1) / _batchSize;

    private int[] BuildPhotoDeck(string folder)
    {
        var order = (int[])_photosByFolder[folder].Clone();

        // Sort key: show count in the high 32 bits, a random value in the low
        // 32. One Array.Sort then gives "least shown first, random order
        // within the same count" without a custom comparer.
        var keys = new long[order.Length];
        for (int i = 0; i < order.Length; i++)
        {
            long shown = Math.Max(0, _showCountOf(order[i]));
            keys[i] = (shown << 32) | (uint)Random.Shared.Next();
        }

        Array.Sort(keys, order);
        return order;
    }

    private static void Shuffle(string[] items)
    {
        for (int i = items.Length - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    private sealed class Deck
    {
        public Deck(int[] order) => Order = order;

        public int[] Order { get; }
        public int Cursor { get; set; }
    }
}
