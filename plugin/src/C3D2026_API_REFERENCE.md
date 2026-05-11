# Civil 3D 2026 API — Quick reference

This file captures the *actual* C3D 2026 .NET API surface (probed from
AeccDbMgd.dll metadata) for the symbols the bridge relies on. Use it when
fixing API-drift errors. Anything not listed here either matches the
older API or wasn't checked.

## CivilDocument
- `GetAlignmentIds()`, `GetSurfaceIds()`, `GetPipeNetworkIds()`
- `CorridorCollection`, `AssemblyCollection`, `SubassemblyCollection` (NO `GetAssemblyIds()`)
- `CogoPoints`, `PointGroups`, `Styles`, `Settings`

## Alignment
- `Entities` (collection of `AlignmentEntity`)
- `PointLocation(double station, double offset, ref double easting, ref double northing)` — **ref** not out
- `StartingStation`, `EndingStation`, `Length`, `Name`, `Description`
- `GetProfileIds()`, `GetSampleLineGroupIds()`

## AlignmentEntity (base)
- `EntityId`, `EntityType`, `SubEntityCount`, `EntityBefore`, `EntityAfter`
- **No** `Length`, `StartStation`, `EndStation` — those are on subclasses:
  - `AlignmentLine`, `AlignmentArc`, `AlignmentCurve` (and Spiral)
  - All have `Length`, `StartStation`, `EndStation`, `StartPoint`, `EndPoint`

## Profile
- `StartingStation`, `EndingStation`, `ElevationMin`, `ElevationMax`, `Length`, `Entities`, `PVIs`
- `ElevationAt(double station)`, `GradeAt(double station)`
- `CreateByLayout(...)` and `CreateFromSurface(...)` overloads exist

## ProfilePVI
- `Station`, `Elevation` — confirmed
- (Did not see `CurveType`, `CurveLength`, `GradeIn`, `GradeOut` in declared members — may inherit from base; treat as suspect)

## ProfileEntity
- `EntityType`, `EntityId`, etc.
- **No** `Grade` on the base — subclasses (`ProfileTangent`, `ProfileCircular`, `ProfileParabola`) hold geometry

## Corridor
- `Baselines`, `Rebuild()`, `IsOutOfDate` (NOT `IsModelOutOfDate`)
- `GetTargets()`, `SetTargets()`
- `CorridorSurfaces`

## Network (Pipe network)
- `Create(...)`, `GetPipeIds()`, `GetStructureIds()`
- `AddLinePipe`, `AddCurvePipe`, `AddStructure`, `MoveParts`, `BreakPipe`
- **No** `AddNetworkPart`, **no** `NetworkPartType` enum
- `PartsListId`, `PartsListName`

## PartsList (Styles.PartsList)
- `GetPartFamilyIdsByDomain(DomainType)` ✓
- `Item[guid]`, `PartFamilyCount`

## PartFamily (Styles.PartFamily)
- `PartSizeCount`, indexer `Item` — iterate via index, NOT `GetPartSizeIds()`
- `PartType`, `Domain`, `GUID`, `Description`
- `AddPartSize`, `RemovePartSize`

## PartSize (Styles.PartSize)
- `PartStyleId`, `RulesStyleId`, `MaterialStyleId`, `SizeDataRecord`, `PayItems`
- **No** `SizeName`, `Description`, `GetAllDataFields()`
- Inner diameter etc. live inside `SizeDataRecord`

## SampleLineGroup
- `GetSampleLineIds()` (NOT `SampleLineIds` property)
- `Create(...)`, `GetSectionSources()`, `GetMaterialSectionSources()`
- **No** `SampledSourceCollection`

## SampleLine
- `Create` overloads: not (string,ObjectId,double,double,double)
- Look up overloads with the probe before using

## Section
- `SectionPoints` (property, NOT method `SampleSectionPoints`)

## TinSurface
- `AddVertex`, `AddVertices` (NOT `AddCgPoints`)
- `Create`, `CreateFromTin`, `CreateFromLandXML`, etc.

## CogoPointCollection
- `Add(Point3d location)` — and 5 other overloads
- Use positional args, NOT `useNextPointNumber:` named arg
- `GetPointByPointNumber(num)` returns `ObjectId` directly, NOT a wrapper with `.ObjectId`

## Surface ambiguity
There is `Autodesk.AutoCAD.DatabaseServices.Surface` AND
`Autodesk.Civil.DatabaseServices.Surface`. Resolve with:
```csharp
using Surface = Autodesk.Civil.DatabaseServices.Surface;
```
at the top of any file that uses both `AutoCAD.DatabaseServices` and Civil's.

## AcadApp.Application Exception
`Autodesk.AutoCAD.Runtime.Exception` shadows `System.Exception`. In
Plugin.cs use the fully-qualified `System.Exception` for catch clauses.
