using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public class AssetStoreLibraryChecker : EditorWindow
{
   private Vector2 _scrollPos;
   private readonly List<AssetEntry> _ownedAssets = new();
   private bool _isLoading;
   private bool _checkRan;
   private string _statusMessage = "Click 'Fetch Assets' to load your library.";

   private int _assetStartIndex;
   private const int ASSET_LIMIT = 50;

   private object _client;
   private MethodInfo _listMethod;
   private Type _queryArgsType;
   private EventInfo _productListFetchedEvent;
   private Delegate _eventDelegate;
   private readonly List<string> _pendingProductIds = new();

   private enum SortColumn
   {
      Name,
      Publisher,
      Flagged
   }

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

      if (_ownedAssets.Count > 0)
      {
         EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
         DrawSortableHeader("Name", SortColumn.Name, 400);
         DrawSortableHeader("Publisher", SortColumn.Publisher, 280);
         if (_checkRan)
            DrawSortableHeader("Flagged", SortColumn.Flagged, 80);
         EditorGUILayout.EndHorizontal();

         _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

         foreach (var asset in _ownedAssets)
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
         EditorGUILayout.LabelField($"Total: {_ownedAssets.Count} assets", EditorStyles.miniLabel);
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
      _ownedAssets.Sort((a, b) =>
      {
         int cmp = _sortColumn switch
         {
            SortColumn.Name => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
            SortColumn.Publisher => string.Compare(a.PublisherName, b.PublisherName,
               StringComparison.OrdinalIgnoreCase),
            SortColumn.Flagged => a.IsFlagged.CompareTo(b.IsFlagged),
            _ => 0
         };
         return _sortAscending ? cmp : -cmp;
      });
   }

   private void FetchAllPurchasedAssets()
   {
      _isLoading = true;
      _ownedAssets.Clear();
      _pendingProductIds.Clear();
      _assetStartIndex = 0;
      _checkRan = false;
      _statusMessage = "Fetching from Asset Store...";
      Repaint();

      try
      {
         var packageManagerType
            = Type.GetType("UnityEditor.PackageManager.UI.Internal.ServicesContainer, UnityEditor");
         var instanceProp = packageManagerType?.GetProperty("instance",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.FlattenHierarchy);
         var container = instanceProp?.GetValue(null);

         var resolveMethod
            = packageManagerType?.GetMethod("Resolve", BindingFlags.Public | BindingFlags.Instance);
         var assetStoreClientType
            = Type.GetType("UnityEditor.PackageManager.UI.Internal.IAssetStoreClient, UnityEditor");
         var resolveGeneric = resolveMethod?.MakeGenericMethod(assetStoreClientType);

         _client = resolveGeneric?.Invoke(container, null);
         _listMethod
            = assetStoreClientType?.GetMethod("ListPurchases",
               BindingFlags.Public | BindingFlags.Instance);
         _queryArgsType
            = Type.GetType("UnityEditor.PackageManager.UI.Internal.PurchasesQueryArgs, UnityEditor");

         if (_eventDelegate == null)
         {
            _productListFetchedEvent = assetStoreClientType?.GetEvent("onProductListFetched",
               BindingFlags.Public | BindingFlags.Instance);
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

            _ownedAssets.Add(new AssetEntry
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
            _statusMessage = $"Got {_ownedAssets.Count} purchases. Fetching details...";
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

         var fetchMethod = clientType.GetMethod("FetchProductInfo",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

         var cacheField = clientType
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(f =>
               f.FieldType.Name.Contains("Cache") || f.FieldType.Name.Contains("cache"));

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

         var getProductInfoMethod = cache.GetType().GetMethod("GetProductInfo",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

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

                     var entry = _ownedAssets.FirstOrDefault(a => a.PackageId == productId.ToString());
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
                     _statusMessage = $"Loaded {_ownedAssets.Count} assets. Checking publishers...";
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

   private static string GetString(Type t, object obj, string member) =>
      (t.GetProperty(member)?.GetValue(obj) ?? t.GetField(member)?.GetValue(obj))?.ToString();

   private void CheckAssets()
   {
      Debug.Log("Looking for assets to be deprecated...");

      foreach (var asset in _ownedAssets)
         asset.IsFlagged = false;

      foreach (var asset in _ownedAssets)
      {
         if (!int.TryParse(asset.PackageId, out var assetID))
         {
            Debug.LogError("Could not parse package ID for asset: " + asset.Name);
         }

         if (PdfData.PackageIDs.Contains(assetID))
         {
            asset.IsFlagged = true;
            Debug.LogWarning($"Found Asset: {asset.Name} from publisher: {asset.PublisherName}");
         }
      }

      int flaggedCount = _ownedAssets.Count(a => a.IsFlagged);
      Debug.Log("Finished searching assets.");

      _checkRan = true;
      _sortColumn = SortColumn.Flagged;
      _sortAscending = false;
      ApplySort();

      _statusMessage = $"Done. {_ownedAssets.Count} assets loaded, {flaggedCount} flagged.";
      Repaint();
   }
}

public static class PdfData
{
   public static readonly int[] PackageIDs =
   {
      112976, 323392, 325702, 335972, 320774, 233059, 229854, 229916, 230204, 226314, 228388, 146062,
      144349, 144557, 144995, 145128, 161894, 163081, 162085, 161493, 161399, 165589, 201388, 157359,
      158079, 157978, 230184, 164012, 158834, 209948, 181215, 180017, 158259, 150402, 158363, 157665,
      149691, 210864, 163382, 163676, 182430, 149265, 158101, 157760, 147745, 22079, 115590, 150042,
      197892, 188973, 64934, 137434, 48289, 56310, 55042, 54287, 66184, 54250, 57291, 202194, 56083,
      66185, 267138, 223199, 261902, 37494, 79782, 12174, 113005, 273290, 344918, 313350, 98259, 99270,
      306424, 307478, 18427, 10813, 60800, 197921, 58183, 173613, 347240, 171399, 19761, 18384, 318948,
      293630, 301082, 303690, 267635, 265188, 295782, 296678, 111195, 149543, 155275, 92962, 297951,
      297916, 297955, 297957, 64068, 130917, 130913, 196338, 113160, 127382, 113389, 113240, 310472,
      299226, 307130, 299610, 63388, 302530, 246320, 90510, 327932, 568, 3068, 22740, 8400, 16461, 643,
      322961, 84673, 221648, 128270, 147273, 99634, 99464, 95677, 160117, 183545, 184960, 99620, 108166,
      108252, 65361, 170799, 285225, 299311, 281067, 304659, 325539, 325540, 324385, 323250, 325495,
      323330, 290811, 294799, 59358, 317912, 323654, 63644, 61400, 308243, 43344, 169876, 220730, 113125,
      130466, 129571, 116689, 93941, 99527, 115340, 72462, 146795, 187775, 76697, 34655, 94754, 91071,
      246998, 242208, 248021, 226684, 246732, 228583, 322080, 330992, 327590, 327261, 332186, 28455,
      126328, 184053, 285102, 269178, 260861, 257650, 277888, 266384, 265176, 269606, 270903, 273137,
      289360, 289361, 289362, 289363, 289364, 275953, 248086, 262332, 258325, 297056, 248156, 167026,
      258612, 255074, 264410, 259919, 278862, 273138, 310144, 281873, 282555, 57744, 129791, 57821,
      162720, 105452, 80947, 83950, 83953, 80952, 80941, 222733, 229227, 210701, 210628, 233895, 210520,
      35515, 58017, 33978, 353686, 331952, 320758, 250678, 67870, 235565, 290662, 287577, 60039, 67924,
      75369, 59353, 64886, 63528, 66393, 67417, 61106, 137901, 62864, 92154, 132115, 163744, 131734,
      196717, 22364, 50608, 22365, 170941, 250262, 138794, 144890, 209011, 294789, 294954, 279640,
      287098, 293097, 279263, 300100, 296186, 322233, 69450, 296569, 296564, 67425, 65566, 70945, 69489,
      343952, 281181, 307545, 307660, 283169, 283241, 286228, 289287, 301563, 307418, 307439, 307532,
      343188, 301759, 330216, 330321, 330329, 330567, 330828, 330838, 342938, 342986, 343106, 348678,
      307805, 365130, 312540, 312738, 316802, 316918, 317003, 317090, 324041, 281118, 287205, 283432,
      196945, 238271, 195209, 233528, 240808, 198513, 188565, 221337, 198834, 198849, 233560, 233562,
      233855, 234548, 258834, 264641, 311787, 184792, 71176, 166115, 114777, 356554, 249283, 225330,
      251792, 245754, 227519, 241911, 242106, 270931, 261316, 225602, 235913, 233422, 251389, 239181,
      236149, 260374, 232511, 232040, 239008, 233180, 240299, 230837, 247129, 245696, 272611, 248959,
      118044, 76492, 97716, 102157, 96768, 131795, 307746, 353782, 356498, 353648, 356496, 150466,
      225217, 274272, 329851, 314907, 255241, 263835, 267639, 272524, 288951, 329864, 296356, 10650,
      151631, 148136, 292194, 318205, 196434, 117992, 121137, 189106, 77024, 76758, 190186, 212601,
      171328, 62338, 283819, 54748, 17380, 126154, 222595, 317854, 247910, 56954, 324644, 364674, 360100,
      360126, 360130, 357928, 356324, 356032, 207203, 140850, 319213, 212385, 306976, 295074, 311111,
      308526, 316897, 84119, 170654, 97756, 170334, 92397, 102696, 91056, 99825, 78829, 78528, 93972,
      78151, 171139, 169794, 77505, 117735, 244556, 244661, 244667, 43414, 56384, 252859, 297898, 297959,
      297713, 137587, 14759, 15501, 18655, 19157, 224288, 193976, 48416, 33015, 222927, 229343, 97617,
      262430, 107830, 107827, 107415, 107825, 107832, 140646, 175442, 166690, 166727, 246579, 264844,
      269015, 92577, 315348, 364658, 90910, 54365, 152804, 162545, 302944, 220264, 220312, 214666, 53485,
      41662, 52953, 67827, 115797, 120232, 106986, 124506, 74553, 109590, 85084, 99525, 194868, 81339,
      118229, 77917, 75028, 326309, 111309, 198153, 71993, 129411, 257383, 57211, 250702, 302093, 302180,
      301811, 128999, 150089, 65478, 49479, 275182, 126138, 126133, 126137, 195116, 183905, 327635,
      314703, 251857, 176415, 293820, 293818, 289146, 174219, 220144, 324486, 269939, 310042, 163664,
      310039, 110046, 148087, 177985, 10401, 12207, 12015, 25635, 10397, 243515, 242330, 240212, 244327,
      247403, 243516, 244328, 240135, 258669, 63784, 80297, 78106, 200428, 286206, 204653, 202708,
      160154, 155701, 118713, 126416, 54774, 130442, 290371, 78028, 98662, 101112, 305316, 290669, 62466,
      20828, 84709, 87318, 23722, 144149, 129090, 84761, 147849, 142391, 142548, 102506, 170331, 119267,
      218574, 106192, 92041, 183645, 116943, 183651, 149082, 85350, 16020, 111205, 101781, 97090, 154444,
      105050, 220998, 196819, 237641, 232762, 98598, 110927, 112477, 131812, 291629, 182351, 302429,
      254434, 338424, 262690, 278370, 309632, 258761, 259527, 217727, 228196, 243178, 213957, 208223,
      118481, 139510, 168877, 97887, 108701, 109667, 117260, 90099, 73850, 116185, 158347, 251250,
      221954, 10884, 206591, 338560, 238784, 271470, 285916, 33610, 251253, 239568, 95106, 14617, 263813,
      77106, 74275, 185308, 99701, 190809, 93872, 225481, 230889, 223772, 272536, 353998, 342486, 329269,
      323189, 363058, 309530, 312419, 348384, 320226, 316521, 301226, 307335, 283515, 289049, 285872,
      273557, 280750, 273660, 295250, 304256, 290922, 297778, 84688, 153320, 226510, 10156, 221285,
      221516, 233553, 286183, 333806, 37363, 57115, 70296, 71378, 18221, 99352, 213571, 85993, 77446,
      87419, 87488, 178583, 92533, 66271, 163993, 164221, 4325, 315476, 305089, 314813, 314764, 314380,
      315491, 315391, 315095, 314895, 247298, 354944, 352328, 23749, 21988, 163269, 294953, 79203,
      273197, 267629, 293303, 173366, 188208, 170918, 169542, 166513, 166369, 164262, 169297, 169977,
      165395, 163368, 168783, 203786, 169406, 174875, 174208, 193184, 179531, 169831, 188098, 261219,
      169082, 189709, 239326, 173233, 178161, 187904, 185538, 199517, 186465, 164583, 182290, 176690,
      259154, 203507, 178783, 190143, 164358, 179044, 174128, 167017, 163265, 169616, 165736, 163622,
      174464, 260149, 187387, 174805, 187703, 208186, 232001, 164484, 166789, 166945, 165886, 182277,
      166683, 185597, 176943, 164646, 174865, 163132, 176515, 185443, 164642, 189693, 189828, 258312,
      185727, 258662, 258247, 174316, 175257, 192361, 186387, 192729, 162288, 169483, 177974, 196649,
      188278, 175810, 175319, 169847, 260326, 168185, 174389, 176305, 167886, 176467, 186937, 187524,
      187921, 176430, 166185, 186636, 166019, 260869, 187413, 192366, 199817, 164483, 167983, 182878,
      168872, 187459, 173558, 186844, 259516, 180061, 253193, 73206, 298720, 290577, 29264, 164035,
      88409, 94708, 94718, 88319, 94700, 88414, 96264, 88315, 88384, 94724, 94699, 91212, 88385, 94622,
      94623, 88405, 94631, 88306, 94640, 88386, 94637, 88387, 88395, 94134, 94726, 88392, 88396, 94728,
      88401, 88402, 94721, 88403, 94704, 94697, 94722, 94731, 94712, 94717, 94702, 94713, 94711, 88408,
      80057, 151097, 109676, 111211, 78349, 87264, 143140, 73888, 176992, 116749, 86098, 63336, 146017,
      94829, 257958, 94831, 258855, 258856, 94832, 126004, 131117, 319894, 94833, 94834, 191393, 63497,
      74752, 71934, 100753, 43200, 87316, 65979, 65980, 65983, 86550, 76611, 76268, 79256, 66063, 66064,
      101899, 59061, 58978, 59074, 61170, 61163, 61162, 61160, 59365, 61159, 59290, 60460, 59073, 59072,
      57919, 62849, 80189, 92681, 64348, 42146, 85166, 61141, 61236, 33635, 86896, 33631, 41532, 42866,
      52746, 292892, 224970, 82276, 89687, 324974, 186625, 50190, 50074, 136712, 149168, 182286, 127651,
      301688, 200979, 178734, 179568, 179262, 191726, 224329, 183426, 242921, 19179, 19219, 21184, 18955,
      18905, 19748, 20793, 19034, 19197, 19353, 20851, 21189, 19380, 19318, 19146, 19538, 277197, 239177,
      202395, 163610, 175018, 161920, 222736, 216636, 118783, 175884, 97585, 157552, 228370, 112941,
      263829, 294560, 133646, 95949, 322538, 323797, 323611, 319347, 321861, 326301, 293592, 293893,
      272873, 303468, 295986, 304655, 306736, 243191, 147495, 242716, 293489, 257372, 302969, 296400,
      300734, 119088, 10599, 73306, 268917, 313639, 298419, 60202, 307419, 326213, 137916, 129282, 90687,
      291237, 293300, 292099, 292100, 293294, 96757, 96687, 71166, 71165, 71168, 71164, 97269, 96737,
      101585, 96732, 100854, 104643, 293634, 104654, 104656, 104660, 104661, 104662, 104663, 293628,
      104664, 72052, 72053, 72054, 72055, 85338, 103509, 295540, 294001, 111377, 104407, 104412, 104163,
      104421, 104420, 104472, 104469, 293940, 111379, 111380, 103510, 111381, 103511, 103513, 103514,
      103515, 294294, 294562, 103516, 294880, 103517, 294878, 103518, 103519, 103520, 294565, 103521,
      294567, 293908, 103523, 103526, 103527, 103528, 294881, 111382, 103529, 103530, 103532, 103531,
      111376, 103533, 111749, 111383, 103534, 103535, 103536, 294018, 294566, 103537, 111384, 103538,
      103541, 294022, 103542, 294877, 103543, 103544, 103545, 103547, 103549, 103551, 111385, 103553,
      103554, 294564, 294876, 103555, 103557, 294882, 103558, 103559, 103560, 103561, 294021, 111386,
      103562, 294019, 103563, 72057, 99453, 72058, 283725, 280906, 284192, 280556, 308465, 198660,
      198083, 198533, 198473, 198507, 198493, 198667, 198629, 198008, 197949, 263784, 265122, 262924,
      94195, 177156, 107172, 174536, 107241, 107249, 174529, 107664, 107610, 176466, 107663, 108560,
      181721, 177405, 124432, 131456, 284729, 204261, 219844, 247725, 262616, 296217, 297548, 297432,
      300198, 297333, 297402, 298504, 300874, 77530, 84327, 77158, 76710, 77097, 156924, 114120, 272609,
      106656, 271699, 271700, 125704, 265404, 56647, 272605, 272610, 54751, 207752, 345108, 123711,
      114199, 116227, 94064, 144399, 207779, 207214, 305389, 77805, 35436, 36310, 37803, 37251, 64223,
      78082, 36881, 173384, 40082, 34654, 29911, 149539, 174580, 98117, 194485, 194524, 195120, 194543,
      153729, 244547, 205683, 86572, 86571, 347474, 120976, 90743, 132312, 246670, 307873, 155596,
      159524, 90374, 315268, 254735, 320761, 360364, 116889, 3832, 171276, 165789, 93166, 24483, 116603,
      102678, 67918, 116553, 76988, 92049, 67621, 125694, 76865, 17275, 53235, 53232, 67399, 222256,
      222880, 164877, 322020, 308280, 309723, 317848, 99411, 132019, 201818, 320345, 341074, 196127,
      196203, 200354, 206220, 206223, 196204, 153369, 136968, 314111, 305333, 305309, 309677, 314113,
      313393, 314116, 241189, 259412, 279234, 125555, 150150, 136230, 150080, 120775, 186750, 135212,
      197909, 119258, 121210, 201977, 324607, 330661, 140416, 138073, 56597, 3810, 194744, 194486,
      188537, 301515, 53205, 301402, 301910, 231547, 299143, 107838, 126037, 143935, 134119, 135032,
      109187, 192021, 119011, 117918, 107952, 117852, 109707, 125992, 134986, 118128, 243584, 276274,
      297538, 275287, 230212, 238422, 239477, 216183, 216132, 216880, 223770, 220562, 215596, 227814,
      230097, 229405, 319923, 159182, 160372, 277340, 78248, 246893, 76968, 68090, 203754, 321729,
      330663, 340760, 346656, 340762, 343174, 334634, 89073, 101435, 127503, 63760, 63085, 101360, 64070,
      101354, 61538, 90660, 42377, 307755, 173922, 165460, 103905, 51316, 55395, 53215, 115970, 116025,
      89470, 46348, 79113, 52957, 52959, 52960, 47523, 100204, 266695, 199154, 242328, 231023, 242321,
      117399, 115904, 118307, 312549, 98425, 214972, 283640, 143185, 342076, 343966, 343970, 343972,
      356154, 364648, 364650, 364652, 364688, 364690, 364692, 360404, 345198, 345272, 345290, 345294,
      345298, 345304, 335296, 333656, 360790, 352488, 358072, 347792, 347584, 333842, 340774, 344190,
      344192, 344194, 344196, 329572, 163343, 207096, 313411, 84420, 84421, 50366, 50372, 50367, 50380,
      50378, 50381, 30115, 58184, 49080, 68144, 68145, 68146, 36270, 38287, 38362, 87678, 69056, 69057,
      69058, 69059, 45781, 157418, 80860, 311042, 312044, 281238, 316531, 335634, 311665, 313494, 308386,
      316985, 322364, 322972, 317926, 320191, 310961, 311125, 313692, 322995, 306129, 303905, 300610,
      304714, 319298, 320182, 322378, 320289, 340350, 288436, 282093, 293637, 348982, 321598, 304712,
      350286, 305301, 188914, 217607, 281891, 72388, 71357, 111724, 135820, 111523, 279639, 57226, 71359,
      221960, 141712, 196627, 183741, 256549, 79499, 241246, 243057, 199879, 148823, 286779, 291268,
      340956, 142681, 144197, 145583, 146406, 144776, 144827, 139930, 147075, 136750, 143559, 134755,
      135669, 140871, 138697, 140601, 142668, 138691, 145103, 142665, 140675, 143349, 139385, 186692,
      189557, 30351, 284058, 116731, 68417, 241533, 74333, 72137, 74746, 74160, 90229, 357054, 17515,
      123298, 82228, 117861, 148049, 148066, 236371, 327027, 348376, 342058, 341316, 330236, 342480,
      330353, 340066, 340482, 349118, 349140, 341028, 349362, 349364, 336688, 339572, 329155, 329249,
      329250, 342068, 313408, 327116, 326579, 327232, 330891, 332952, 333330, 335670, 328101, 327673,
      325310, 333474, 331084, 350598, 326308, 70489, 71086, 105445, 245807, 53056, 14151, 297669, 297676,
      297674, 297136, 297241, 297810, 297671, 296576, 296694, 296885, 296780, 297601, 296962, 297058,
      297814, 297816, 297590, 297591, 297817, 297812, 297532, 297592, 297593, 297594, 297330, 297442,
      302974, 178334, 158255, 277551, 133688, 243948, 350420, 308325, 313630, 303904, 306858, 308031,
      288972, 273074, 291473, 287267, 287264, 291590, 288976, 288965, 292670, 288942, 287250, 291374,
      287820, 286938, 291589, 291559, 246666, 102517, 190349, 28860, 102841, 39505, 97577, 12373, 41395,
      95676, 105389, 62550, 96209, 154091, 154113, 97358, 231682, 230540, 277705, 278253, 278746, 278750,
      271063, 94491, 38187, 178865, 133926, 238606, 267341, 234478, 268213, 236738, 347408, 343350, 3113,
      17048, 9480, 36495, 53241, 17508, 10083, 223781, 222241, 154331, 310719, 310720, 310721, 310723,
      310639, 310724, 310727, 310716, 310728, 318180, 317673, 224988, 225013, 225009, 225015, 316249,
      66388, 73346, 71250, 40370, 65492, 43825, 48484, 61325, 61642, 148212, 149307, 178576, 194736,
      206303, 150596, 160361, 182009, 146165, 296117, 160790, 195265, 142345, 257576, 301525, 253492,
      287855, 285418, 257489, 290103, 287492, 257620, 293076, 292568, 289237, 305600, 294520, 295339,
      33754, 28708, 48185, 174673, 178037, 175839, 168724, 11462, 83539, 291329, 195969, 195640, 322092,
      140334, 139929, 152968, 141608, 180923, 145715, 145719, 145767, 180929, 180928, 175613, 85359,
      116515, 140520, 124377, 158560, 161379, 155896, 104605, 168599, 183051, 182476, 138079, 137000,
      189252, 185860, 310131, 179696, 112560, 226509, 169084, 207393, 180373, 238945, 139817, 180638,
      226430, 199426, 234264, 215842, 241893, 239453, 238054, 287115, 105672, 160889, 297509, 292585,
      315905, 311412, 297663, 298423, 312722, 298415, 319502, 316099, 330932, 327140, 319524, 327142,
      327234, 319531, 325545, 325546, 319527, 334850, 316533, 319500, 322888, 322889, 316004, 316206,
      329350, 315946, 315802, 303528, 309229, 315660, 315596, 317296, 316908, 316907, 28088, 122499,
      336696, 328372, 225055, 208439, 253864, 212257, 96136, 164818, 103191, 250109, 95073, 107992,
      107997, 107939, 83103, 238152, 77098, 90295, 41583, 299312, 110074, 358206, 355628, 281679, 283711,
      246217, 125444, 132607, 137980, 139422, 152149, 124564, 149732, 168389, 345346, 356302, 85897,
      131567, 92514, 131458, 118232, 134044, 118231, 128428, 121787, 110040, 48957, 107472, 125688,
      75055, 72216, 73577, 100818, 80979, 70814, 121229, 121327, 157965, 156723, 117120, 156506, 198402,
      287731, 329312, 198108, 245855, 279195, 209660, 209572, 164417, 178348
   };
}