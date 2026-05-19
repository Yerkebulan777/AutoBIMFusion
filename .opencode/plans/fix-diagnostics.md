# Fix 6 Code Diagnostics

## 1. CA1510 — `src/AutoBIMFusion.Common/Extensions/DBObject.cs:10`

**Replace:**
```csharp
if (dbObj == null)
{
    throw new ArgumentNullException(nameof(dbObj));
}
```
**With:**
```csharp
ArgumentNullException.ThrowIfNull(dbObj);
```

---

## 2. CA1847 — `src/AutoBIMFusion.Common/Mist/Geometry/CotePoints.cs:157`

**Replace:**
```csharp
if (OriginalString.Contains("%"))
```
**With:**
```csharp
if (OriginalString.Contains('%'))
```

---

## 3. CA2211 — `src/AutoBIMFusion.Common/Mist/Geometry/PolygonOperations/Slice.cs:8`

**Replace:**
```csharp
public static List<Polyline>? LastSliceResult;
```
**With:**
```csharp
public static List<Polyline>? LastSliceResult { get; private set; }
```

---

## 4. SYSLIB1054 — `src/AutoBIMFusion.Common/Helpers/WindowsNaturalComparer.cs:17`

**Replace:**
```csharp
using System.Runtime.InteropServices;
...
[DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
private static extern int StrCmpLogicalW(string x, string y);
```
**With:**
```csharp
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
...
[LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
private static partial int StrCmpLogicalW(string x, string y);
```

Note: `LibraryImport` requires the method to be `partial`.

---

## 5. CA1859 — `src/AutoBIMFusion.Merge/Combine/Layouts/LayoutProjectionProcessor.cs:197`

**Replace:**
```csharp
private static List<ObjectId> CollectPaperEntityIds(
    BlockTableRecord btr,
    RXClass viewportClass,
    IReadOnlySet<ObjectId> clipEntityIds)
```
**With:**
```csharp
private static List<ObjectId> CollectPaperEntityIds(
    BlockTableRecord btr,
    RXClass viewportClass,
    HashSet<ObjectId> clipEntityIds)
```

The caller at line 166 already passes a `HashSet<ObjectId>`.

---

## 6. CA1869 — `src/AutoBIMFusion.Plugin/Commands/CombineCommands.cs:240-243`

**Add static field** after `_mergeGate`:
```csharp
private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
```

**Replace in `WriteBatchStatus`:**
```csharp
JsonSerializerOptions options = new()
{
    WriteIndented = true
};

File.WriteAllText(statusPath, JsonSerializer.Serialize(payload, options));
```
**With:**
```csharp
File.WriteAllText(statusPath, JsonSerializer.Serialize(payload, _jsonOptions));
```
