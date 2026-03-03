using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public class AssetStoreLibraryChecker : EditorWindow
{
   private Vector2 _scrollPos;
   private readonly List<AssetEntry> _assets = new();
   private bool _isLoading;
   private bool _checkRan;
   private string _statusMessage = "Click 'Fetch Assets' to load your library.";

   private int _assetStartIndex;
   private const int ASSET_LIMIT = 50;
   private const string CSV_FILE_PATH = "Assets/assets_removed.csv";

   private object _client;
   private MethodInfo _listMethod;
   private Type _queryArgsType;
   private EventInfo _productListFetchedEvent;
   private Delegate _eventDelegate;
   private readonly List<string> _pendingProductIds = new();

   private readonly List<AssetEntry> _allEntries = new();

   private enum SortColumn { Name, Publisher, Flagged }
   private SortColumn _sortColumn = SortColumn.Name;
   private bool _sortAscending = true;

   [Serializable]
   private class AssetEntry
   {
      public string Name;
      public string PackageId;
      public string PublisherName;
      public bool IsFlagged;
   }

   [MenuItem("Window/My Asset Store Library")]
   public static void ShowWindow()
   {
      var window = GetWindow<AssetStoreLibraryChecker>("Asset Store Library");
      window.minSize = new Vector2(600, 400);
   }

   private void OnGUI()
   {
      EditorGUILayout.Space(6);

      if (GUILayout.Button(_isLoading ? "Loading..." : "Fetch All My Assets", GUILayout.Height(30)))
      {
         if (!_isLoading)
            FetchAllPurchasedAssets();
      }

      EditorGUILayout.Space(4);
      EditorGUILayout.LabelField(_statusMessage, EditorStyles.helpBox, GUILayout.Height(40));

      if (_assets.Count > 0)
      {
         EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
         DrawSortableHeader("Name", SortColumn.Name, 400);
         DrawSortableHeader("Publisher", SortColumn.Publisher, 280);
         if (_checkRan)
            DrawSortableHeader("Flagged", SortColumn.Flagged, 80);
         EditorGUILayout.EndHorizontal();

         _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

         foreach (var asset in _assets)
         {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(asset.Name, GUILayout.Width(400));
            EditorGUILayout.LabelField(asset.PublisherName, GUILayout.Width(280));
            if (_checkRan)
               EditorGUILayout.LabelField(asset.IsFlagged ? "Yes" : "", GUILayout.Width(80));
            EditorGUILayout.EndHorizontal();
         }

         EditorGUILayout.EndScrollView();
         EditorGUILayout.Space(4);
         EditorGUILayout.LabelField($"Total: {_assets.Count} assets", EditorStyles.miniLabel);
      }
   }

   private void DrawSortableHeader(string label, SortColumn column, float width)
   {
      string display = _sortColumn == column
         ? label + (_sortAscending ? " ▲" : " ▼")
         : label;

      if (GUILayout.Button(display, EditorStyles.toolbarButton, GUILayout.Width(width)))
      {
         if (_sortColumn == column)
            _sortAscending = !_sortAscending;
         else
         {
            _sortColumn = column;
            _sortAscending = true;
         }
         ApplySort();
      }
   }

   private void ApplySort()
   {
      _assets.Sort((a, b) =>
      {
         int cmp = _sortColumn switch
         {
            SortColumn.Name      => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
            SortColumn.Publisher => string.Compare(a.PublisherName, b.PublisherName, StringComparison.OrdinalIgnoreCase),
            SortColumn.Flagged   => a.IsFlagged.CompareTo(b.IsFlagged),
            _                    => 0
         };
         return _sortAscending ? cmp : -cmp;
      });
   }

   private void FetchAllPurchasedAssets()
   {
      _isLoading = true;
      _assets.Clear();
      _pendingProductIds.Clear();
      _assetStartIndex = 0;
      _checkRan = false;
      _statusMessage = "Fetching from Asset Store...";
      Repaint();

      try
      {
         var packageManagerType = Type.GetType("UnityEditor.PackageManager.UI.Internal.ServicesContainer, UnityEditor");
         var instanceProp = packageManagerType?.GetProperty("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
         var container = instanceProp?.GetValue(null);

         var resolveMethod = packageManagerType?.GetMethod("Resolve", BindingFlags.Public | BindingFlags.Instance);
         var assetStoreClientType = Type.GetType("UnityEditor.PackageManager.UI.Internal.IAssetStoreClient, UnityEditor");
         var resolveGeneric = resolveMethod?.MakeGenericMethod(assetStoreClientType);

         _client = resolveGeneric?.Invoke(container, null);
         _listMethod = assetStoreClientType?.GetMethod("ListPurchases", BindingFlags.Public | BindingFlags.Instance);
         _queryArgsType = Type.GetType("UnityEditor.PackageManager.UI.Internal.PurchasesQueryArgs, UnityEditor");

         if (_eventDelegate == null)
         {
            _productListFetchedEvent = assetStoreClientType?.GetEvent("onProductListFetched", BindingFlags.Public | BindingFlags.Instance);
            if (_productListFetchedEvent != null)
            {
               var handler = new Action<object>(OnProductListFetched);
               var delegateType = _productListFetchedEvent.EventHandlerType;
               _eventDelegate = Delegate.CreateDelegate(delegateType, handler.Target, handler.Method);
               _productListFetchedEvent.AddEventHandler(_client, _eventDelegate);
            }
         }

         FetchPage(_assetStartIndex);
      }
      catch (Exception ex)
      {
         Debug.LogError($"[AssetStoreLibrary] Reflection error: {ex}");
         _statusMessage = "Error setting up fetch.";
         _isLoading = false;
      }
   }

   private void FetchPage(int startIndex)
   {
      try
      {
         _statusMessage = $"Fetching assets {startIndex} to {startIndex + ASSET_LIMIT}...";
         var queryArgs = Activator.CreateInstance(_queryArgsType, startIndex, ASSET_LIMIT, null, null);
         _listMethod.Invoke(_client, queryArgs != null ? new[] { queryArgs } : new object[] { 0, null });
      }
      catch (Exception ex)
      {
         Debug.LogError($"[AssetStoreLibrary] FetchPage error: {ex}");
         _statusMessage = "Error fetching page.";
         _isLoading = false;
      }
   }

   private void OnProductListFetched(object result)
   {
      try
      {
         var resultType = result.GetType();
         var listProp = resultType.GetField("list");
         var items = listProp?.GetValue(result) as System.Collections.IEnumerable;

         if (items == null) return;

         var enumerable = items.Cast<object>().ToList();

         foreach (var item in enumerable)
         {
            var t = item.GetType();
            var productId = GetString(t, item, "productId") ?? "";
            var displayName = GetString(t, item, "displayName") ?? productId;

            _assets.Add(new AssetEntry
            {
               Name = displayName.Trim(),
               PackageId = productId,
               PublisherName = "",
            });

            if (!string.IsNullOrEmpty(productId))
               _pendingProductIds.Add(productId);
         }

         if (enumerable.Count == ASSET_LIMIT)
         {
            _assetStartIndex += ASSET_LIMIT;
            EditorApplication.delayCall += () => FetchPage(_assetStartIndex);
         }
         else
         {
            _statusMessage = $"Got {_assets.Count} purchases. Fetching details...";
            Repaint();
            EditorApplication.delayCall += FetchAllDetails;
         }
      }
      catch (Exception ex)
      {
         _statusMessage = $"Error parsing results: {ex.Message}";
         Debug.LogError($"[AssetStoreLibrary] Parse error: {ex}");
         _isLoading = false;
      }
      finally
      {
         Repaint();
      }
   }

   private void FetchAllDetails()
   {
      try
      {
         var clientType = _client.GetType();

         var fetchMethod = clientType.GetMethod("FetchProductInfo", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

         var cacheField = clientType
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(f => f.FieldType.Name.Contains("Cache") || f.FieldType.Name.Contains("cache"));

         var cache = cacheField?.GetValue(_client);

         if (cache == null)
         {
            Debug.LogError("Could not find cache on client. Dumping fields:");
            foreach (var f in clientType.GetFields(BindingFlags.Public | BindingFlags.NonPublic |
                                                   BindingFlags.Instance))
               Debug.Log($"  [FIELD] {f.Name} ({f.FieldType.Name})");
            _statusMessage = "Could not find asset cache. Check console.";
            _isLoading = false;
            Repaint();
            return;
         }

         var getProductInfoMethod = cache.GetType().GetMethod("GetProductInfo", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

         int remaining = _pendingProductIds.Count;

         foreach (var idStr in _pendingProductIds.Where(id => long.TryParse(id, out _)))
         {
            long productId = long.Parse(idStr);

            fetchMethod?.Invoke(_client, new object[] { productId, (Action)DoneCallback });

            continue;

            void DoneCallback()
            {
               try
               {
                  var productInfo = getProductInfoMethod?.Invoke(cache, new object[] { productId });
                  if (productInfo != null)
                  {
                     var t = productInfo.GetType();
                     var publisher = GetString(t, productInfo, "publisherName") ?? "";

                     var entry = _assets.FirstOrDefault(a => a.PackageId == productId.ToString());
                     if (entry != null)
                        entry.PublisherName = publisher.Trim();
                  }
               }
               catch (Exception ex)
               {
                  Debug.LogError($"[AssetStoreLibrary] Detail callback error: {ex}");
               }
               finally
               {
                  remaining--;
                  if (remaining <= 0)
                  {
                     _isLoading = false;
                     _statusMessage = $"Loaded {_assets.Count} assets. Checking publishers...";
                     CheckAssets();
                  }

                  Repaint();
               }
            }
         }
      }
      catch (Exception ex)
      {
         Debug.LogError($"[AssetStoreLibrary] FetchAllDetails error: {ex}");
         _statusMessage = $"Detail fetch error: {ex.Message}";
         _isLoading = false;
         Repaint();
      }
   }

   private static string GetString(Type t, object obj, string member) => (t.GetProperty(member)?.GetValue(obj) ?? t.GetField(member)?.GetValue(obj))?.ToString();

   private void CheckAssets()
   {
      if (!TryLoadCSV(CSV_FILE_PATH))
      {
         Debug.LogError($"CSV file not found at: {CSV_FILE_PATH}");
         _statusMessage = $"Loaded {_assets.Count} assets. CSV not found — skipping check.";
         Repaint();
         return;
      }

      Debug.Log("Looking for assets to be deprecated...");

      foreach (var asset in _assets)
         asset.IsFlagged = false;

      foreach (var asset in _assets)
      {
         foreach (var publishedAsset in _allEntries)
         {
            if (string.Equals(asset.PublisherName, publishedAsset.PublisherName, StringComparison.InvariantCultureIgnoreCase))
            {
               asset.IsFlagged = true;
               Debug.LogWarning($"Found Asset: {asset.Name} from publisher: {asset.PublisherName}");
            }
         }
      }

      int flaggedCount = _assets.Count(a => a.IsFlagged);
      Debug.Log("Finished searching assets.");

      _checkRan = true;
      _sortColumn = SortColumn.Flagged;
      _sortAscending = false;
      ApplySort();

      _statusMessage = $"Done. {_assets.Count} assets loaded, {flaggedCount} flagged.";
      Repaint();
   }

   private bool TryLoadCSV(string path)
   {
      _allEntries.Clear();

      if (!File.Exists(path))
         return false;

      string[] lines = File.ReadAllLines(path);

      // Skip header row (index 0)
      for (int i = 1; i < lines.Length; i++)
      {
         string line = lines[i].Trim();
         if (string.IsNullOrEmpty(line)) continue;

         string[] columns = SplitCSVLine(line);

         if (columns.Length < 2) continue;

         _allEntries.Add(new AssetEntry
         {
            Name          = columns[0].Trim(),
            PackageId     = string.Empty,
            PublisherName = columns[1].Trim()
         });
      }

      Debug.Log("Loaded entries from CSV.");

      return true;

      string[] SplitCSVLine(string line)
      {
         var result = new List<string>();
         var current = new System.Text.StringBuilder();

         foreach (var c in line)
         {
            if (c == ',')
            {
               result.Add(current.ToString());
               current.Clear();
            }
            else
            {
               current.Append(c);
            }
         }

         result.Add(current.ToString()); // last field
         return result.ToArray();
      }
   }
}
