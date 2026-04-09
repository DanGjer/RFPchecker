namespace RFPchecker;

public class RevitSpace
{
    public long SpaceElementId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public double Area { get; set; }
    public double Volume { get; set; }
    public string DrofusRoomIdentifier { get; set; } = string.Empty;
}

public class RevitElectricalFixture
{
    public long ElementId { get; set; }
    public string UniqueId { get; set; } = string.Empty;
    public string FamilyName { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string SUS_Antall_Stikkontaktuttak { get; set; } = string.Empty;
    public string Krafttype { get; set; } = string.Empty;
    public string Formaal { get; set; } = string.Empty;
    public string SpaceName { get; set; } = string.Empty;
    public string SpaceNumber { get; set; } = string.Empty;
    public string DrofusRoomIdentifier { get; set; } = string.Empty;
    public long SpaceElementId { get; set; }
    public string SpaceResolutionMethod { get; set; } = string.Empty;
}

public class RevitDataDevice
{
    public long ElementId { get; set; }
    public string UniqueId { get; set; } = string.Empty;
    public string FamilyName { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string SUS_Antall_Datauttak { get; set; } = string.Empty;
    public string Formaal { get; set; } = string.Empty;
    public string SpaceName { get; set; } = string.Empty;
    public string SpaceNumber { get; set; } = string.Empty;
    public string DrofusRoomIdentifier { get; set; } = string.Empty;
    public long SpaceElementId { get; set; }
    public string SpaceResolutionMethod { get; set; } = string.Empty;
}

public static class RevitCollectors
{
    private const string RevitSpaceComparisonKeyParameter = "BSN_RomNrFunk";

    public static List<RevitSpace> CollectSpaces(Document doc, AssistantArgs args)
    {
        return new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_MEPSpaces)
            .WhereElementIsNotElementType()
            .Cast<Space>()
            .Select(s => new
            {
                Space = s,
                RoomIdentifier = s.LookupParameter(RevitSpaceComparisonKeyParameter)?.AsString(),
                AreaValue = s.get_Parameter(BuiltInParameter.ROOM_AREA)?.AsValueString(),
                VolumeValue = s.get_Parameter(BuiltInParameter.ROOM_VOLUME)?.AsValueString()
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.RoomIdentifier))
            .Where(x =>
                !string.Equals(x.AreaValue, "Not Enclosed", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(x.AreaValue, "Redundant Space", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(x.VolumeValue, "Not Enclosed", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(x.VolumeValue, "Redundant Space", StringComparison.OrdinalIgnoreCase))
            .Select(x => new RevitSpace
            {
                SpaceElementId = x.Space.Id.Value,
                Name = x.Space.Name,
                Number = x.Space.Number,
                Area = x.Space.Area,
                Volume = x.Space.Volume,
                DrofusRoomIdentifier = x.RoomIdentifier!
            })
            .ToList();
    }

    public static List<RevitElectricalFixture> CollectElectricalFixtures(Document doc)
    {
        var spaces = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_MEPSpaces)
            .WhereElementIsNotElementType()
            .Cast<Space>()
            .ToList();

        return new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_ElectricalFixtures)
            .WhereElementIsNotElementType()
            .OfType<FamilyInstance>()
            .Select(fixture =>
            {
                var space = ResolveFixtureSpace(fixture, spaces, out var resolutionMethod);

                return new RevitElectricalFixture
                {
                    ElementId = fixture.Id.Value,
                    UniqueId = fixture.UniqueId,
                    FamilyName = fixture.Symbol?.FamilyName ?? fixture.Symbol?.Family?.Name ?? string.Empty,
                    TypeName = fixture.Symbol?.Name ?? string.Empty,
                    SUS_Antall_Stikkontaktuttak = GetParameterValueAsString(fixture.Symbol?.LookupParameter("SUS_Antall Stikkontaktuttak")),
                    Krafttype = GetParameterValueAsString(fixture.LookupParameter("Krafttype")),
                    Formaal = GetParameterValueAsString(fixture.LookupParameter("Formaal")),
                    SpaceName = space?.Name ?? string.Empty,
                    SpaceNumber = space?.Number ?? string.Empty,
                    DrofusRoomIdentifier = GetParameterValueAsString(space?.LookupParameter(RevitSpaceComparisonKeyParameter)),
                    SpaceElementId = space?.Id.Value ?? 0,
                    SpaceResolutionMethod = resolutionMethod
                };
            })
            .Where(fixture =>
                !string.IsNullOrWhiteSpace(fixture.SUS_Antall_Stikkontaktuttak))
            .ToList();
    }

    public static List<RevitDataDevice> CollectDataDevices(Document doc)
    {
        var spaces = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_MEPSpaces)
            .WhereElementIsNotElementType()
            .Cast<Space>()
            .ToList();

        return new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_DataDevices)
            .WhereElementIsNotElementType()
            .OfType<FamilyInstance>()
            .Select(device =>
            {
                var space = ResolveFixtureSpace(device, spaces, out var resolutionMethod);

                return new RevitDataDevice
                {
                    ElementId = device.Id.Value,
                    UniqueId = device.UniqueId,
                    FamilyName = device.Symbol?.FamilyName ?? device.Symbol?.Family?.Name ?? string.Empty,
                    TypeName = device.Symbol?.Name ?? string.Empty,
                    SUS_Antall_Datauttak = GetParameterValueAsString(device.Symbol?.LookupParameter("SUS_Antall Datauttak")),
                    Formaal = GetParameterValueAsString(device.LookupParameter("Formaal")),
                    SpaceName = space?.Name ?? string.Empty,
                    SpaceNumber = space?.Number ?? string.Empty,
                    DrofusRoomIdentifier = GetParameterValueAsString(space?.LookupParameter(RevitSpaceComparisonKeyParameter)),
                    SpaceElementId = space?.Id.Value ?? 0,
                    SpaceResolutionMethod = resolutionMethod
                };
            })
            .Where(device => !string.IsNullOrWhiteSpace(device.SUS_Antall_Datauttak))
            .ToList();
    }

    private static Space? ResolveFixtureSpace(FamilyInstance fixture, List<Space> spaces, out string resolutionMethod)
    {
        var nativeSpace = fixture.Space;
        if (nativeSpace != null)
        {
            resolutionMethod = "Native";
            return nativeSpace;
        }

        var lookupPoint = GetFixtureLookupPoint(fixture);
        if (lookupPoint == null)
        {
            resolutionMethod = "Unresolved";
            return null;
        }

        var pointResolvedSpace = spaces.FirstOrDefault(space => space.IsPointInSpace(lookupPoint));
        if (pointResolvedSpace != null)
        {
            resolutionMethod = "PointLookup";
            return pointResolvedSpace;
        }

        // Retry with small offsets to catch fixtures placed just outside the space boundary.
        // Revit internal units are feet: 0.0328084 ft ≈ 10 mm
        double[] offsets = [0.0328084, 0.0984252, 0.032808]; // 10 mm, 30 mm, 10 mm in XY
        XYZ[] directions = [XYZ.BasisZ, XYZ.BasisZ, -XYZ.BasisZ];

        for (int i = 0; i < offsets.Length; i++)
        {
            var offsetPoint = lookupPoint + directions[i] * offsets[i];
            var offsetSpace = spaces.FirstOrDefault(space => space.IsPointInSpace(offsetPoint));
            if (offsetSpace != null)
            {
                resolutionMethod = "PointLookupWithOffset";
                return offsetSpace;
            }
        }

        resolutionMethod = "Unresolved";
        return null;
    }

    private static XYZ? GetFixtureLookupPoint(FamilyInstance fixture)
    {
        if (fixture.Location is LocationPoint locationPoint)
            return locationPoint.Point;

        if (fixture.Location is LocationCurve locationCurve)
            return locationCurve.Curve?.Evaluate(0.5, true);

        var boundingBox = fixture.get_BoundingBox(null);
        if (boundingBox == null)
            return null;

        return (boundingBox.Min + boundingBox.Max) / 2.0;
    }

    private static string GetParameterValueAsString(Parameter? parameter)
    {
        if (parameter == null)
            return string.Empty;

        var displayValue = parameter.AsValueString();
        if (!string.IsNullOrWhiteSpace(displayValue))
            return displayValue;

        return parameter.StorageType switch
        {
            StorageType.String => parameter.AsString() ?? string.Empty,
            StorageType.Integer => parameter.AsInteger().ToString(),
            StorageType.Double => parameter.AsDouble().ToString(),
            StorageType.ElementId => parameter.AsElementId().Value.ToString(),
            _ => string.Empty
        };
    }
}
