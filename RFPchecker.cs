using dRofusClient.Occurrences;
using dRofusClient.Rooms;

namespace RFPchecker;

public class RFPcheckerCommand : IRevitExtension<AssistantArgs>
{
    private const string RfpTextTypeName = "RFPchecker_Note";

        public IExtensionResult Run(IRevitExtensionContext context, AssistantArgs args, CancellationToken cancellationToken)
        {
            var uiDocument = context.UIApplication.ActiveUIDocument;
            var document = uiDocument?.Document;

            if (document is null)
                return Result.Text.Failed("Revit has no active model open");

            var activeView = document.ActiveView;
            if (activeView == null)
                return Result.Text.Failed("No active view found");

            var viewLevel = activeView.GenLevel;
            if (viewLevel == null)
                return Result.Text.Failed("Active view has no associated level");

            var listOfSpaces = RevitCollectors.CollectSpaces(document, args);
            listOfSpaces = listOfSpaces
                .Where(space =>
                {
                    try
                    {
                        var spaceElement = document.GetElement(new ElementId(space.SpaceElementId)) as SpatialElement;
                        if (spaceElement == null)
                            return false;
                        var spaceLevel = spaceElement.Level;
                        return spaceLevel != null && spaceLevel.Id == viewLevel.Id;
                    }
                    catch { return false; }
                })
                .ToList();

            if (args.Mode == AnalysisMode.AnalyzeSelected)
            {
                if (uiDocument == null)
                    return Result.Text.Failed("No active UI document.");

                var selectedIds = new HashSet<long>(uiDocument.Selection.GetElementIds().Select(id => id.Value));
                listOfSpaces = listOfSpaces
                    .Where(space => selectedIds.Contains(space.SpaceElementId))
                    .ToList();

                if (listOfSpaces.Count == 0)
                    return Result.Text.Failed("No spaces selected on the active level.");
            }

            if (args.Mode == AnalysisMode.Reset)
            {
                using var resetTx = new Transaction(document, "RFPchecker reset colors and notes");
                resetTx.Start();

                DeleteTextNotesOnLevel(document, activeView);

                foreach (var space in listOfSpaces)
                {
                    ClearSpaceColorOverride(activeView, space.SpaceElementId);
                }

                resetTx.Commit();

                return Result.Text.Succeeded($"Reset colors and removed RFP notes for {listOfSpaces.Count} spaces.");
            }

            var client = new dRofusClientFactory().Create(document);

        var queryRooms = Query.List()
        .Select("id","name","room_func_no","drawing_no","room_data_20101610","room_data_20102210","room_data_20102310","room_data_21101010")
        .Filter(Filter.StartsWith("architect_no", "76."));

        var allRooms = client.GetRooms(queryRooms);

        var allRoomsJson = System.Text.Json.JsonSerializer.Serialize(allRooms, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });

        var roomResponses = System.Text.Json.JsonSerializer.Deserialize<List<DrofusRoomResponse>>(allRoomsJson, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];

        var drofusRooms = roomResponses
            .Select(room => new DrofusRoom
            {
                DrofusRoomId = room.Id.ToString(),
                DrofusRoomName = room.Name ?? string.Empty,
                DrofusRoomFuncNo = DrofusRoomResponse.AsText(room.RoomFuncNo),
                DrofusDrawingNo = DrofusRoomResponse.AsText(room.DrawingNo),
                DrofusOutletsNormal = DrofusRoomResponse.AsText(room.RoomData20101610),
                DrofusOutletsEmergency = DrofusRoomResponse.AsText(room.RoomData20102210),
                DrofusOutletsUps = DrofusRoomResponse.AsText(room.RoomData20102310),
                DrofusOutletsData = DrofusRoomResponse.AsText(room.RoomData21101010)
            })
            .ToList();

        var allFixtures = RevitCollectors.CollectElectricalFixtures(document);
        var allDataDevices = RevitCollectors.CollectDataDevices(document);

        var statusCounts = new Dictionary<RoomStatus, int>
        {
            { RoomStatus.Ok, 0 },
            { RoomStatus.Deficit, 0 },
            { RoomStatus.Over, 0 },
            { RoomStatus.UndefinedOutlets, 0 },
            { RoomStatus.Unmatched, 0 }
        };

        using var tx = new Transaction(document, "RFPchecker room status color");
        tx.Start();

        // Clear only prior notes created by this tool in the active view.
        if (args.Mode != AnalysisMode.AnalyzeSelected)
            DeleteTextNotesOnLevel(document, activeView);

        foreach (var space in listOfSpaces)
        {
            var drofusMatch = drofusRooms.FirstOrDefault(r => string.Equals(r.DrofusRoomFuncNo, space.DrofusRoomIdentifier, StringComparison.OrdinalIgnoreCase));

            var fixturesForSpace = allFixtures.Where(f => string.Equals(f.DrofusRoomIdentifier, space.DrofusRoomIdentifier, StringComparison.OrdinalIgnoreCase)).ToList();
            var devicesForSpace = allDataDevices.Where(d => string.Equals(d.DrofusRoomIdentifier, space.DrofusRoomIdentifier, StringComparison.OrdinalIgnoreCase)).ToList();

            var normalOutlets = 0;
            var emergencyOutlets = 0;
            var upsOutlets = 0;
            var dataOutlets = 0;
            var dedicatedElkraftOutlets = 0;
            var dedicatedDataOutlets = 0;
            var missingPowerTypeOutlets = 0;

            foreach (var fixture in fixturesForSpace)
            {
                // Power outlets (SUS_Antall_Stikkontaktuttak)
                var powerOutletCount = ParseOutletCount(fixture.SUS_Antall_Stikkontaktuttak);
                if (powerOutletCount > 0)
                {
                    if (string.IsNullOrWhiteSpace(fixture.Krafttype))
                    {
                        missingPowerTypeOutlets += powerOutletCount;
                    }
                    else if (!string.IsNullOrWhiteSpace(fixture.Formaal))
                    {
                        if (GetOutletType(fixture.Krafttype) == OutletType.Data)
                            dedicatedDataOutlets += powerOutletCount;
                        else
                            dedicatedElkraftOutlets += powerOutletCount;
                    }
                    else
                    {
                        switch (GetOutletType(fixture.Krafttype))
                        {
                            case OutletType.Normal:
                                normalOutlets += powerOutletCount;
                                break;
                            case OutletType.Emergency:
                                emergencyOutlets += powerOutletCount;
                                break;
                            case OutletType.Ups:
                                upsOutlets += powerOutletCount;
                                break;
                            case OutletType.Data:
                                dataOutlets += powerOutletCount;
                                break;
                        }
                    }
                }

                // Data outlets (SUS_Antall_Datauttak)
                var fixtureDataCount = ParseOutletCount(fixture.SUS_Antall_Datauttak);
                if (fixtureDataCount > 0)
                {
                    if (!string.IsNullOrWhiteSpace(fixture.Formaal))
                        dedicatedDataOutlets += fixtureDataCount;
                    else
                        dataOutlets += fixtureDataCount;
                }
            }

            foreach (var device in devicesForSpace)
            {
                var outletCount = ParseOutletCount(device.SUS_Antall_Datauttak);
                if (outletCount <= 0)
                    continue;

                if (!string.IsNullOrWhiteSpace(device.Formaal))
                {
                    dedicatedDataOutlets += outletCount;
                    continue;
                }

                dataOutlets += outletCount;
            }

            var requiredNormal = drofusMatch == null ? 0 : ParseOutletCount(drofusMatch.DrofusOutletsNormal);
            var requiredEmergency = drofusMatch == null ? 0 : ParseOutletCount(drofusMatch.DrofusOutletsEmergency);
            var requiredUps = drofusMatch == null ? 0 : ParseOutletCount(drofusMatch.DrofusOutletsUps);
            var requiredData = drofusMatch == null ? 0 : ParseOutletCount(drofusMatch.DrofusOutletsData);

            var status = DetermineRoomStatus(
                drofusMatch != null,
                normalOutlets,
                emergencyOutlets,
                upsOutlets,
                dataOutlets,
                requiredNormal,
                requiredEmergency,
                requiredUps,
                requiredData);

            // Missing Krafttype is a separate status so undefined outlets are explicitly visible.
            if (drofusMatch != null && missingPowerTypeOutlets > 0)
                status = RoomStatus.UndefinedOutlets;

            statusCounts[status]++;
            var statusColor = GetStatusColor(status);
            ApplySpaceColorOverride(document, activeView, space.SpaceElementId, statusColor);

            try
            {
                var statusLabel = status switch
                {
                    RoomStatus.Ok => "OK",
                    RoomStatus.Deficit => "Under RFP",
                    RoomStatus.Over => "Over RFP",
                    RoomStatus.UndefinedOutlets => "Udef. uttak",
                    _ => "Unmatched"
                };
                var drawingNo = string.IsNullOrWhiteSpace(drofusMatch?.DrofusDrawingNo) ? "-" : drofusMatch.DrofusDrawingNo;
                var feedbackText = $"{space.Name} / {drawingNo}\nRFP Status: {statusLabel}\nN:{normalOutlets}/{requiredNormal} Nød:{emergencyOutlets}/{requiredEmergency} U:{upsOutlets}/{requiredUps} D:{dataOutlets}/{requiredData}\nDE:{dedicatedElkraftOutlets} DD:{dedicatedDataOutlets} MKT:{missingPowerTypeOutlets}";
                CreateSpaceTextNote(document, activeView, space.SpaceElementId, feedbackText);
            }
            catch { }
        }

        tx.Commit();

        var totalRooms = listOfSpaces.Count;
        var resultMessage = $"Colored {totalRooms} rooms: {statusCounts[RoomStatus.Ok]} OK, {statusCounts[RoomStatus.Deficit]} DEFICIT, {statusCounts[RoomStatus.Over]} OVER, {statusCounts[RoomStatus.UndefinedOutlets]} UDEF. UTTAK, {statusCounts[RoomStatus.Unmatched]} UNMATCHED";

        return Result.Text.Succeeded(resultMessage);


    }

    private static RoomStatus DetermineRoomStatus(
        bool hasDrofusMatch,
        int foundNormal,
        int foundEmergency,
        int foundUps,
        int foundData,
        int requiredNormal,
        int requiredEmergency,
        int requiredUps,
        int requiredData)
    {
        if (!hasDrofusMatch)
            return RoomStatus.Unmatched;

        if (foundNormal < requiredNormal ||
            foundEmergency < requiredEmergency ||
            foundUps < requiredUps ||
            foundData < requiredData)
            return RoomStatus.Deficit;

        if (foundNormal > requiredNormal ||
            foundEmergency > requiredEmergency ||
            foundUps > requiredUps ||
            foundData > requiredData)
            return RoomStatus.Over;

        return RoomStatus.Ok;
    }

    private static Color GetStatusColor(RoomStatus status)
    {
        return status switch
        {
            RoomStatus.Ok => new Color(60, 180, 75),
            RoomStatus.Deficit => new Color(230, 25, 75),
            RoomStatus.Over => new Color(0, 100, 255),
            RoomStatus.UndefinedOutlets => new Color(245, 130, 49),
            _ => new Color(160, 160, 160)
        };
    }

    private static void ApplySpaceColorOverride(Document doc, View view, long spaceElementId, Color color)
    {
        var fillPattern = new FilteredElementCollector(doc)
            .OfClass(typeof(FillPatternElement))
            .Cast<FillPatternElement>()
            .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);

        if (fillPattern == null)
            return;

        var overrides = new OverrideGraphicSettings();
        overrides.SetSurfaceForegroundPatternVisible(true);
        overrides.SetSurfaceForegroundPatternId(fillPattern.Id);
        overrides.SetSurfaceForegroundPatternColor(color);
        overrides.SetSurfaceBackgroundPatternVisible(true);
        overrides.SetSurfaceBackgroundPatternId(fillPattern.Id);
        overrides.SetSurfaceBackgroundPatternColor(color);

        overrides.SetCutForegroundPatternVisible(true);
        overrides.SetCutForegroundPatternId(fillPattern.Id);
        overrides.SetCutForegroundPatternColor(color);
        overrides.SetCutBackgroundPatternVisible(true);
        overrides.SetCutBackgroundPatternId(fillPattern.Id);
        overrides.SetCutBackgroundPatternColor(color);

        overrides.SetProjectionLineColor(color);

        view.SetElementOverrides(new ElementId(spaceElementId), overrides);
    }

    private static void ClearSpaceColorOverride(View view, long spaceElementId)
    {
        view.SetElementOverrides(new ElementId(spaceElementId), new OverrideGraphicSettings());
    }

    private static int ParseOutletCount(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return 0;

        if (int.TryParse(rawValue.Trim(), out var parsedInt))
            return parsedInt;

        var normalized = rawValue.Trim().Replace(',', '.');
        if (double.TryParse(normalized, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedDouble))
            return (int)Math.Round(parsedDouble, MidpointRounding.AwayFromZero);

        return 0;
    }

    private static OutletType GetOutletType(string? krafttype)
    {
        if (string.IsNullOrWhiteSpace(krafttype))
            return OutletType.Unknown;

        var normalized = krafttype.Trim().ToLowerInvariant();

        if (normalized.Contains("ups"))
            return OutletType.Ups;

        if (normalized.Contains("nød") || normalized.Contains("nod") || normalized.Contains("emergency"))
            return OutletType.Emergency;

        if (normalized.Contains("data"))
            return OutletType.Data;

        if (normalized.Contains("normal"))
            return OutletType.Normal;

        return OutletType.Unknown;
    }

    private static void CreateSpaceTextNote(Document doc, View view, long spaceElementId, string text)
    {
        var spaceElement = doc.GetElement(new ElementId(spaceElementId));
        if (spaceElement == null)
            return;

        var centerPoint = GetSpaceCenter(spaceElement);
        if (centerPoint == null)
            return;

        var textType = EnsureRfpTextType(doc);

        if (textType == null)
            return;

        var options = new TextNoteOptions
        {
            HorizontalAlignment = HorizontalTextAlignment.Center,
            VerticalAlignment = VerticalTextAlignment.Middle,
            TypeId = textType.Id
        };

        TextNote.Create(doc, view.Id, centerPoint, 0.5, text, options);
    }

    private static XYZ? GetSpaceCenter(Element spaceElement)
    {
        try
        {
            var boundingBox = spaceElement.get_BoundingBox(null);
            if (boundingBox == null)
                return null;

            var min = boundingBox.Min;
            var max = boundingBox.Max;

            return new XYZ(
                (min.X + max.X) / 2,
                (min.Y + max.Y) / 2,
                (min.Z + max.Z) / 2
            );
        }
        catch
        {
            return null;
        }
    }

    private static TextNoteType? EnsureRfpTextType(Document doc)
    {
        var existing = new FilteredElementCollector(doc)
            .OfClass(typeof(TextNoteType))
            .Cast<TextNoteType>()
            .FirstOrDefault(t => string.Equals(t.Name, RfpTextTypeName, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
            return existing;

        var baseType = new FilteredElementCollector(doc)
            .OfClass(typeof(TextNoteType))
            .Cast<TextNoteType>()
            .FirstOrDefault();

        return baseType == null ? null : (TextNoteType)baseType.Duplicate(RfpTextTypeName);
    }

    private static ElementId? TryGetRfpTextTypeId(Document doc)
    {
        var textType = new FilteredElementCollector(doc)
            .OfClass(typeof(TextNoteType))
            .Cast<TextNoteType>()
            .FirstOrDefault(t => string.Equals(t.Name, RfpTextTypeName, StringComparison.OrdinalIgnoreCase));

        return textType?.Id;
    }

    private static void DeleteTextNotesOnLevel(Document doc, View activeView)
    {
        try
        {
            var rfpTextTypeId = TryGetRfpTextTypeId(doc);
            if (rfpTextTypeId == null)
                return;

            var allTextNotes = new FilteredElementCollector(doc, activeView.Id)
                .OfClass(typeof(TextNote))
                .Cast<TextNote>()
                .Where(tn => tn.GetTypeId() == rfpTextTypeId)
                .Select(tn => tn.Id)
                .ToList();

            foreach (var id in allTextNotes)
            {
                doc.Delete(id);
            }
        }
        catch { }
    }

    private enum OutletType
    {
        Unknown,
        Normal,
        Emergency,
        Ups,
        Data
    }

    private enum RoomStatus
    {
        Unmatched,
        Deficit,
        Over,
        UndefinedOutlets,
        Ok
    }

}
