using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Energy7.StationSurvey.Utility
{
    /// <summary>
    /// Material Positioning workbook (.xlsx): Summary + BOM, Location-wise, Asset-wise, Point list, Sensor matrix.
    /// Every table has a coloured, bordered two-level header, frozen header rows, filters and status colours.
    /// </summary>
    public static class MaterialPositioningExcel
    {
        // Header group colours (dark = group band, light = column header)
        private static readonly XLColor Brand = XLColor.FromHtml("#2C0E0E");
        private static readonly XLColor Coral = XLColor.FromHtml("#E16349");
        private static readonly (XLColor dark, XLColor light) GId = (XLColor.FromHtml("#44546A"), XLColor.FromHtml("#D6DCE4"));
        private static readonly (XLColor dark, XLColor light) GPlan = (XLColor.FromHtml("#C0504D"), XLColor.FromHtml("#F2DCDB"));
        private static readonly (XLColor dark, XLColor light) GCard = (XLColor.FromHtml("#B7791F"), XLColor.FromHtml("#FCE9C8"));
        private static readonly (XLColor dark, XLColor light) GInput = (XLColor.FromHtml("#0F7B5F"), XLColor.FromHtml("#D5EFE6"));
        private static readonly (XLColor dark, XLColor light) GWired = (XLColor.FromHtml("#1F5F99"), XLColor.FromHtml("#DAE8F5"));
        private static readonly (XLColor dark, XLColor light) GNote = (XLColor.FromHtml("#595959"), XLColor.FromHtml("#E7E6E6"));
        private static readonly (XLColor dark, XLColor light) GVolt = (XLColor.FromHtml("#7030A0"), XLColor.FromHtml("#E9DDF3"));
        private static readonly (XLColor dark, XLColor light) GCurr = (XLColor.FromHtml("#C55A11"), XLColor.FromHtml("#FBE2D5"));

        // Status colours
        private static readonly XLColor OkFill = XLColor.FromHtml("#E2F0D9"), OkFont = XLColor.FromHtml("#375623");
        private static readonly XLColor GapFill = XLColor.FromHtml("#FFF2CC"), GapFont = XLColor.FromHtml("#9C5700");
        private static readonly XLColor NoFill = XLColor.FromHtml("#FBE5E5"), NoFont = XLColor.FromHtml("#9C0006");
        private static readonly XLColor InfoFill = XLColor.FromHtml("#DDEBF7"), InfoFont = XLColor.FromHtml("#1F4E78");
        private static readonly XLColor TotalFill = XLColor.FromHtml("#F2F2F2");

        // Asset-type band colours
        private static XLColor TypeFill(string type) => MaterialPositioning.TypeOrder(type) switch
        {
            1 => XLColor.FromHtml("#E2F0D9"),   // Track
            2 => XLColor.FromHtml("#FBE5E5"),   // Signal
            3 => XLColor.FromHtml("#DDEBF7"),   // Point machine
            4 => XLColor.FromHtml("#EDE4F5"),   // Axle counter
            _ => XLColor.FromHtml("#EDEDED")
        };

        public static byte[] Build(StationMaterialPositioning m)
        {
            using var wb = new XLWorkbook();
            wb.Style.Font.FontName = "Calibri";
            wb.Style.Font.FontSize = 10;

            Summary(wb.AddWorksheet("Summary"), m);
            LocationWise(wb.AddWorksheet("Location-wise"), m);
            AssetWise(wb.AddWorksheet("Asset-wise"), m);
            PointList(wb.AddWorksheet("Point list"), m);
            SensorMatrix(wb.AddWorksheet("Sensor matrix"), m);

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        // ══ Sheets ═════════════════════════════════════════════════════════════════

        private static void Summary(IXLWorksheet ws, StationMaterialPositioning m)
        {
            int r = Title(ws, m, "Summary and bill of material", 8);
            var surveyed = m.Locations.Where(l => l.HasSurvey).ToList();

            // Station information
            r = Band(ws, r, 1, 2, "Station information", GId.dark);
            var info = new (string, string)[]
            {
                ("Station", $"{m.StationName}{(string.IsNullOrWhiteSpace(m.StationCode) ? "" : $" ({m.StationCode})")}"),
                ("Zone / Division", m.ZoneDivision),
                ("Locations", $"{m.Locations.Count}  ({surveyed.Count} surveyed, {m.Locations.Count - surveyed.Count} wired only)"),
                ("Assets", m.Assets.Count.ToString()),
                ("Generated", m.GeneratedAt.ToString("dd-MMM-yyyy HH:mm")),
                ("Prepared by", m.PreparedBy),
                ("Spare", $"{m.SparePercent:0.##} %")
            };
            int top = r;
            foreach (var (k, v) in info)
            {
                Set(ws.Cell(r, 1), k).Style.Font.SetBold().Fill.SetBackgroundColor(GId.light);
                Set(ws.Cell(r, 2), v);
                r++;
            }
            Box(ws.Range(top - 1, 1, r - 1, 2));

            // Key figures (right of station information)
            int kr = top - 1;
            Band(ws, kr, 4, 5, "Key figures (planned from survey)", GPlan.dark);
            var kpi = new (string, object)[]
            {
                ("Data points", m.Locations.Sum(l => l.HasSurvey ? l.Plan.Voltage + l.Plan.Current : l.WiredPoints.Count)),
                ("Voltage points", surveyed.Sum(l => l.Plan.Voltage)),
                ("Current points", surveyed.Sum(l => l.Plan.Current)),
                ("Voltage cards (MintCard)", surveyed.Sum(l => l.Plan.VoltageCards)),
                ("Current cards (C)", surveyed.Sum(l => l.Plan.CurrentCards)),
                ("A10 = ADC", surveyed.Sum(l => l.Plan.A10)),
                ("Locations with gaps", m.Locations.Count(l => l.Gaps.Count > 0))
            };
            int k2 = kr + 1;
            foreach (var (k, v) in kpi)
            {
                Set(ws.Cell(k2, 4), k).Style.Font.SetBold().Fill.SetBackgroundColor(GPlan.light);
                Set(ws.Cell(k2, 5), v).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Font.SetBold();
                k2++;
            }
            Box(ws.Range(kr, 4, k2 - 1, 5));
            r = Math.Max(r, k2) + 1;

            // Bill of material
            r = Band(ws, r, 1, 7, "Bill of material", Coral);
            var hdr = new[] { "#", "Material", "Unit", "Calculated", "Spare", "Required", "Wired (Yard Config)" };
            for (int c = 0; c < hdr.Length; c++) HeaderCell(ws.Cell(r, c + 1), hdr[c], GPlan.light);
            int bomTop = r; r++;
            string? group = null;
            foreach (var b in m.Bom)
            {
                if (b.Group != group)
                {
                    group = b.Group;
                    var g = ws.Range(r, 1, r, 7).Merge();
                    Set(g.FirstCell(), group.ToUpperInvariant()).Style.Font.SetBold().Font.SetFontColor(Brand).Fill.SetBackgroundColor(XLColor.FromHtml("#FFF4F1"));
                    r++;
                }
                Set(ws.Cell(r, 1), b.No); Set(ws.Cell(r, 2), b.Material); Set(ws.Cell(r, 3), b.Unit);
                Set(ws.Cell(r, 4), b.Calculated); Set(ws.Cell(r, 5), b.Spare);
                Set(ws.Cell(r, 6), b.Required).Style.Font.SetBold();
                if (b.Actual.HasValue)
                {
                    Set(ws.Cell(r, 7), b.Actual.Value);
                    Status(ws.Cell(r, 7), b.Actual.Value == b.Calculated ? "ok" : b.Actual.Value == 0 ? "no" : "gap");
                }
                else Set(ws.Cell(r, 7), "—");
                ws.Range(r, 3, r, 7).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                r++;
            }
            Grid(ws.Range(bomTop, 1, r - 1, 7));
            r++;

            // Legend
            r = Band(ws, r, 1, 7, "Colour legend", GNote.dark);
            var legend = new (string, XLColor, XLColor)[]
            {
                ("Planned from survey", GPlan.light, XLColor.Black), ("Cards / A10 / ADC", GCard.light, XLColor.Black),
                ("Inputs (attributes)", GInput.light, XLColor.Black), ("Wired in Yard Config", GWired.light, XLColor.Black),
                ("Matches survey", OkFill, OkFont), ("Differs from survey", GapFill, GapFont),
                ("Not wired yet", NoFill, NoFont), ("Wired only (no survey row)", InfoFill, InfoFont)
            };
            int lt = r;
            foreach (var (txt, fill, font) in legend)
            {
                ws.Cell(r, 1).Style.Fill.SetBackgroundColor(fill);
                Set(ws.Cell(r, 2), txt).Style.Font.SetFontColor(font);
                r++;
            }
            Grid(ws.Range(lt, 1, r - 1, 2));
            r++;

            // Rules
            r = Band(ws, r, 1, 7, "Calculation rules", GNote.dark);
            var rules = new[]
            {
                "Sensor points per gear come from AttributeSensorMapping (datalogger attributes are not in it, so they are not planned).",
                "Track: TR column = relay end (Vr, TPR V (Loc), Ir mA); TF column = feed end.",
                "Signal: aspect = last word of each entry (\"S-37 HG\"); names starting \"SH\" = shunt; \"CO\" = calling-on.",
                "Point machine: A end always; B end only for a full machine (half / series from survey point details).",
                "Voltage card (AI / MintCard) = voltage ÷ 8: lines 1–8 voltage, lines 9–10 current (blank on axle-counter cards).",
                "Current card = remaining current ÷ 10.  1 card = 1 A10 = 1 ADC.  FTU type = cards × 10.",
                "Which points share a card is decided at wiring time; quantities are for planning and dispatch."
            };
            foreach (var t in rules)
            {
                var rg = ws.Range(r, 1, r, 7).Merge();
                Set(rg.FirstCell(), "•  " + t);
                rg.Style.Alignment.SetWrapText();
                ws.Row(r).Height = 15;
                r++;
            }

            ws.Column(1).Width = 24; ws.Column(2).Width = 40; ws.Column(3).Width = 9;
            ws.Column(4).Width = 26; ws.Column(5).Width = 12; ws.Column(6).Width = 11; ws.Column(7).Width = 19;
            ws.Column(1).Style.Alignment.SetVertical(XLAlignmentVerticalValues.Top);
        }

        private static void LocationWise(IXLWorksheet ws, StationMaterialPositioning m)
        {
            int r = Title(ws, m, "Location-wise material positioning", 22);
            // Two-level header
            var groups = new (string name, int from, int to, (XLColor dark, XLColor light) col)[]
            {
                ("Location", 1, 5, GId), ("Planned from survey", 6, 9, GPlan), ("Cards", 10, 15, GCard),
                ("Inputs", 16, 17, GInput), ("Wired in Yard Config", 18, 22, GWired)
            };
            var cols = new[]
            {
                "Sr", "Cluster", "Type", "Power", "Status",
                "Data points", "No_V", "No_C", "No_DI",
                "Type card", "MintCard", "C card", "A10", "ADC", "FTU type",
                "Voltage inputs (VI)", "Current inputs (C)",
                "Data points", "No_V", "No_C", "Type card", "FTU type"
            };
            r = TwoLevelHeader(ws, r, groups, cols);
            int first = r;
            int sr = 1;
            foreach (var l in m.Locations)
            {
                var p = l.Plan;
                bool hw = l.WiredPoints.Count > 0;
                string status = !l.HasSurvey ? "Wired only" : !hw ? "Not wired" : l.Gaps.Count > 0 ? "Gap" : "OK";
                Set(ws.Cell(r, 1), sr++);
                Set(ws.Cell(r, 2), MaterialPositioning.Label(l)).Style.Font.SetBold();
                Set(ws.Cell(r, 3), l.LocationType);
                Set(ws.Cell(r, 4), l.Power);
                Set(ws.Cell(r, 5), status); Status(ws.Cell(r, 5), status);
                if (l.HasSurvey)
                {
                    Set(ws.Cell(r, 6), p.Voltage + p.Current); Set(ws.Cell(r, 7), p.Voltage); Set(ws.Cell(r, 8), p.Current); Set(ws.Cell(r, 9), 0);
                    Set(ws.Cell(r, 10), p.TypeCard); Set(ws.Cell(r, 11), p.VoltageCards); Set(ws.Cell(r, 12), p.CurrentCards);
                    Set(ws.Cell(r, 13), p.A10); Set(ws.Cell(r, 14), p.Adc); Set(ws.Cell(r, 15), p.FtuChannels);
                    Set(ws.Cell(r, 16), ByAsset(p.Points.Where(x => x.Kind == MaterialPositioning.KindVoltage).Select(x => (x.Asset, x.Title))));
                    Set(ws.Cell(r, 17), ByAsset(p.Points.Where(x => x.Kind == MaterialPositioning.KindCurrent).Select(x => (x.Asset, x.Title))));
                }
                else
                {
                    for (int c = 6; c <= 15; c++) Set(ws.Cell(r, c), "—");
                    Set(ws.Cell(r, 16), ByAsset(l.WiredPoints.Where(x => x.Kind == MaterialPositioning.KindVoltage).Select(x => (x.Asset, x.Title))));
                    Set(ws.Cell(r, 17), ByAsset(l.WiredPoints.Where(x => x.Kind == MaterialPositioning.KindCurrent).Select(x => (x.Asset, x.Title))));
                }
                if (hw)
                {
                    Set(ws.Cell(r, 18), l.WiredPoints.Count); Set(ws.Cell(r, 19), l.WiredVoltageAll); Set(ws.Cell(r, 20), l.WiredCurrentAll);
                    Set(ws.Cell(r, 21), l.WiredTypeCard); Set(ws.Cell(r, 22), l.WiredFtuChannels);
                    if (l.HasSurvey)
                    {
                        Compare(ws.Cell(r, 18), l.WiredPoints.Count, p.Voltage + p.Current);
                        Compare(ws.Cell(r, 19), l.WiredVoltageAll, p.Voltage);
                        Compare(ws.Cell(r, 20), l.WiredCurrentAll, p.Current);
                        if (l.WiredTypeCard != p.TypeCard) Status(ws.Cell(r, 21), "gap");
                    }
                }
                else for (int c = 18; c <= 22; c++) Set(ws.Cell(r, c), "—");

                ws.Range(r, 16, r, 17).Style.Alignment.SetWrapText().Font.SetFontSize(9);
                r++;
            }
            // Total
            var sv = m.Locations.Where(l => l.HasSurvey).ToList();
            Set(ws.Cell(r, 2), "TOTAL");
            Set(ws.Cell(r, 6), sv.Sum(l => l.Plan.Voltage + l.Plan.Current)); Set(ws.Cell(r, 7), sv.Sum(l => l.Plan.Voltage));
            Set(ws.Cell(r, 8), sv.Sum(l => l.Plan.Current)); Set(ws.Cell(r, 9), 0);
            Set(ws.Cell(r, 10), $"C({sv.Sum(l => l.Plan.CurrentCards)}), MintCard({sv.Sum(l => l.Plan.VoltageCards)})");
            Set(ws.Cell(r, 11), sv.Sum(l => l.Plan.VoltageCards)); Set(ws.Cell(r, 12), sv.Sum(l => l.Plan.CurrentCards));
            Set(ws.Cell(r, 13), sv.Sum(l => l.Plan.A10)); Set(ws.Cell(r, 14), sv.Sum(l => l.Plan.Adc)); Set(ws.Cell(r, 15), sv.Sum(l => l.Plan.FtuChannels));
            Set(ws.Cell(r, 18), m.Locations.Sum(l => l.WiredPoints.Count)); Set(ws.Cell(r, 19), m.Locations.Sum(l => l.WiredVoltageAll));
            Set(ws.Cell(r, 20), m.Locations.Sum(l => l.WiredCurrentAll)); Set(ws.Cell(r, 22), m.Locations.Sum(l => l.WiredFtuChannels));
            TotalRow(ws.Range(r, 1, r, 22));

            Grid(ws.Range(first - 2, 1, r, 22));
            Center(ws.Range(first, 1, r, 1)); Center(ws.Range(first, 3, r, 15)); Center(ws.Range(first, 18, r, 22));
            ws.Range(first, 1, r, 22).Style.Alignment.SetVertical(XLAlignmentVerticalValues.Top);
            Widths(ws, 5, 22, 9, 8, 11, 9, 7, 7, 7, 16, 9, 7, 6, 6, 8, 60, 45, 9, 7, 7, 16, 8);
            ws.SheetView.FreezeRows(first - 1);
            ws.SheetView.FreezeColumns(2);
            ws.Range(first - 1, 1, r - 1, 22).SetAutoFilter();
            Notes(ws, r + 2, m.Locations.Where(l => l.Gaps.Count > 0 || l.Plan.Warnings.Count > 0)
                .Select(l => (MaterialPositioning.Label(l), string.Join("  |  ", l.Gaps.Concat(l.Plan.Warnings)))), 22);
        }

        private static void AssetWise(IXLWorksheet ws, StationMaterialPositioning m)
        {
            int r = Title(ws, m, "Asset-wise material positioning", 13);
            var groups = new (string, int, int, (XLColor, XLColor))[]
            {
                ("Asset", 1, 4, GId), ("Planned from survey", 5, 6, GPlan), ("Inputs and sensors", 7, 9, GInput), ("Wired in Yard Config", 10, 13, GWired)
            };
            var cols = new[] { "Sr", "Asset type", "Asset", "Location(s)", "No_V", "No_C", "Voltage inputs", "Current inputs", "Sensors required",
                               "No_V", "No_C", "Location(s)", "Status" };
            r = TwoLevelHeader(ws, r, groups, cols);
            int first = r, sr = 1;
            foreach (var g in m.Assets.GroupBy(a => string.IsNullOrWhiteSpace(a.AssetType) ? "OTHER" : a.AssetType.ToUpperInvariant()))
            {
                // Asset-type band
                var band = ws.Range(r, 1, r, 13).Merge();
                Set(band.FirstCell(), $"{g.Key}  ({g.Count()} assets · {g.Sum(a => a.InSurvey ? a.Voltage : a.WiredVoltage)} V · {g.Sum(a => a.InSurvey ? a.Current : a.WiredCurrent)} C)");
                band.Style.Font.SetBold().Font.SetFontSize(11).Fill.SetBackgroundColor(TypeFill(g.Key));
                r++;
                foreach (var a in g)
                {
                    bool hw = a.WiredVoltage + a.WiredCurrent > 0;
                    string status = !a.InSurvey ? "Wired only" : !hw ? "Not wired"
                                  : a.WiredVoltage != a.Voltage || a.WiredCurrent != a.Current || !SameSet(a.WiredLocations, a.Locations) ? "Gap" : "OK";
                    Set(ws.Cell(r, 1), sr++);
                    Set(ws.Cell(r, 2), a.AssetType);
                    Set(ws.Cell(r, 3), a.Asset).Style.Font.SetBold();
                    Set(ws.Cell(r, 4), string.Join("\n", a.Locations));
                    if (a.InSurvey) { Set(ws.Cell(r, 5), a.Voltage); Set(ws.Cell(r, 6), a.Current); }
                    else { Set(ws.Cell(r, 5), "—"); Set(ws.Cell(r, 6), "—"); }
                    Set(ws.Cell(r, 7), string.Join(", ", a.VoltageInputs));
                    Set(ws.Cell(r, 8), string.Join(", ", a.CurrentInputs));
                    Set(ws.Cell(r, 9), string.Join("\n", a.Sensors.Select(s => $"{SensorLabel(s.Key)} × {s.Value}")));
                    Set(ws.Cell(r, 10), hw ? a.WiredVoltage : 0); Set(ws.Cell(r, 11), hw ? a.WiredCurrent : 0);
                    Set(ws.Cell(r, 12), string.Join("\n", a.WiredLocations));
                    Set(ws.Cell(r, 13), status); Status(ws.Cell(r, 13), status);
                    if (a.InSurvey && hw) { Compare(ws.Cell(r, 10), a.WiredVoltage, a.Voltage); Compare(ws.Cell(r, 11), a.WiredCurrent, a.Current); }
                    ws.Range(r, 4, r, 12).Style.Alignment.SetWrapText();
                    r++;
                }
            }
            var sv = m.Assets.Where(a => a.InSurvey).ToList();
            Set(ws.Cell(r, 3), "TOTAL");
            Set(ws.Cell(r, 5), sv.Sum(a => a.Voltage)); Set(ws.Cell(r, 6), sv.Sum(a => a.Current));
            Set(ws.Cell(r, 10), m.Assets.Sum(a => a.WiredVoltage)); Set(ws.Cell(r, 11), m.Assets.Sum(a => a.WiredCurrent));
            TotalRow(ws.Range(r, 1, r, 13));

            Grid(ws.Range(first - 2, 1, r, 13));
            Center(ws.Range(first, 1, r, 1)); Center(ws.Range(first, 5, r, 6)); Center(ws.Range(first, 10, r, 11)); Center(ws.Range(first, 13, r, 13));
            ws.Range(first, 1, r, 13).Style.Alignment.SetVertical(XLAlignmentVerticalValues.Top);
            Widths(ws, 5, 15, 14, 20, 7, 7, 50, 34, 24, 7, 7, 20, 11);
            ws.SheetView.FreezeRows(first - 1);
            ws.SheetView.FreezeColumns(3);
        }

        private static void PointList(IXLWorksheet ws, StationMaterialPositioning m)
        {
            int r = Title(ws, m, "Point list — every input in sequence", 12);
            var groups = new (string, int, int, (XLColor, XLColor))[]
            {
                ("Location", 1, 2, GId), ("Point (planned from survey)", 3, 8, GPlan), ("Wired in Yard Config", 9, 12, GWired)
            };
            var cols = new[] { "Sr", "Cluster", "Asset type", "Asset", "Attribute", "Kind", "Sensor range", "Planned card",
                               "Card", "Line", "Kind", "Status" };
            r = TwoLevelHeader(ws, r, groups, cols);
            int first = r, sr = 1;
            foreach (var l in m.Locations)
            {
                var label = MaterialPositioning.Label(l);
                var wired = l.WiredPoints.ToList();
                // Location band
                var band = ws.Range(r, 1, r, 12).Merge();
                Set(band.FirstCell(), l.HasSurvey
                    ? $"{label}   ·   {l.Plan.Voltage} V + {l.Plan.Current} C = {l.Plan.Voltage + l.Plan.Current} points   ·   {l.Plan.TypeCard}   ·   A10/ADC {l.Plan.A10}"
                    : $"{label}   ·   wired only (no survey row)   ·   {l.WiredPoints.Count} points   ·   {l.WiredTypeCard}");
                band.Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(GId.dark);
                r++;

                var planned = l.HasSurvey ? l.Plan.Points : new List<MaterialPositioning.SensorPoint>();
                // Voltage points take MintCard lines 1-8 in order; current fills lines 9-10, then C cards (a planning guide)
                var cardOf = PlannedCards(l.Plan, planned);
                foreach (var p in planned)
                {
                    var w = wired.FirstOrDefault(x => string.Equals(x.Asset, p.Asset, StringComparison.OrdinalIgnoreCase)
                                                   && string.Equals(x.Title, p.Title, StringComparison.OrdinalIgnoreCase));
                    if (w != null) wired.Remove(w);
                    string status = w != null ? "Wired" : l.WiredPoints.Count == 0 ? "Not wired" : "Missing";
                    Row(r, sr++, label, p.AssetType, p.Asset, p.Title, p.Kind, p.Type, cardOf.GetValueOrDefault(p) ?? "", w, status, TypeFill(p.AssetType));
                    r++;
                }
                foreach (var w in wired)   // wired but not planned from the survey
                {
                    Row(r, sr++, label, w.AssetType, w.Asset, w.Title, w.Kind, w.Type, "", w, l.HasSurvey ? "Extra" : "Wired only", TypeFill(w.AssetType));
                    r++;
                }
            }
            Grid(ws.Range(first - 2, 1, r - 1, 12));
            Center(ws.Range(first, 1, r - 1, 1)); Center(ws.Range(first, 6, r - 1, 12));
            Widths(ws, 6, 24, 15, 14, 24, 9, 14, 14, 13, 6, 9, 11);
            ws.SheetView.FreezeRows(first - 1);
            ws.Range(first - 1, 1, r - 1, 12).SetAutoFilter();

            void Row(int row, int no, string label, string type, string asset, string title, string kind, string range, string plannedCard,
                     WiredPoint? w, string status, XLColor typeFill)
            {
                Set(ws.Cell(row, 1), no); Set(ws.Cell(row, 2), label);
                Set(ws.Cell(row, 3), type).Style.Fill.SetBackgroundColor(typeFill);
                Set(ws.Cell(row, 4), asset).Style.Font.SetBold();
                Set(ws.Cell(row, 5), title); Set(ws.Cell(row, 6), kind);
                ws.Cell(row, 6).Style.Font.SetFontColor(kind == MaterialPositioning.KindVoltage ? GVolt.dark : GCurr.dark);
                Set(ws.Cell(row, 7), range); Set(ws.Cell(row, 8), plannedCard);
                Set(ws.Cell(row, 9), w?.Card ?? ""); Set(ws.Cell(row, 10), w == null ? "" : w.Line.ToString());
                Set(ws.Cell(row, 11), w?.Kind ?? "");
                Set(ws.Cell(row, 12), status); Status(ws.Cell(row, 12), status);
            }
        }

        private static void SensorMatrix(IXLWorksheet ws, StationMaterialPositioning m)
        {
            var sv = m.Locations.Where(l => l.HasSurvey).ToList();
            var keys = MaterialPositioning.OrderSensors(sv.SelectMany(l => l.Plan.Sensors.Keys)).ToList();
            var vKeys = keys.Where(k => k.StartsWith(MaterialPositioning.KindVoltage)).ToList();
            var cKeys = keys.Where(k => !k.StartsWith(MaterialPositioning.KindVoltage)).ToList();
            int last = 4 + vKeys.Count + cKeys.Count + 2;
            int r = Title(ws, m, "Sensor matrix — sensors required per location", Math.Max(last, 8));

            var groups = new List<(string, int, int, (XLColor, XLColor))> { ("Location", 1, 4, GId) };
            if (vKeys.Count > 0) groups.Add(("Voltage sensors", 5, 4 + vKeys.Count, GVolt));
            if (cKeys.Count > 0) groups.Add(("Current sensors", 5 + vKeys.Count, 4 + vKeys.Count + cKeys.Count, GCurr));
            groups.Add(("Total", last - 1, last, GPlan));
            var cols = new List<string> { "Sr", "Cluster", "Type", "Power" };
            cols.AddRange(vKeys.Select(k => k.Split('|')[1])); cols.AddRange(cKeys.Select(k => k.Split('|')[1]));
            cols.Add("Voltage"); cols.Add("Current");
            r = TwoLevelHeader(ws, r, groups.ToArray(), cols.ToArray());
            int first = r, sr = 1;
            foreach (var l in sv)
            {
                Set(ws.Cell(r, 1), sr++); Set(ws.Cell(r, 2), MaterialPositioning.Label(l)).Style.Font.SetBold();
                Set(ws.Cell(r, 3), l.LocationType); Set(ws.Cell(r, 4), l.Power);
                int c = 5;
                foreach (var k in keys)
                {
                    var n = l.Plan.Sensors.GetValueOrDefault(k);
                    if (n > 0) Set(ws.Cell(r, c), n);
                    c++;
                }
                Set(ws.Cell(r, last - 1), l.Plan.Voltage).Style.Font.SetBold();
                Set(ws.Cell(r, last), l.Plan.Current).Style.Font.SetBold();
                r++;
            }
            Set(ws.Cell(r, 2), "TOTAL");
            int cc = 5;
            foreach (var k in keys) { Set(ws.Cell(r, cc), sv.Sum(l => l.Plan.Sensors.GetValueOrDefault(k))); cc++; }
            Set(ws.Cell(r, last - 1), sv.Sum(l => l.Plan.Voltage)); Set(ws.Cell(r, last), sv.Sum(l => l.Plan.Current));
            TotalRow(ws.Range(r, 1, r, last));

            Grid(ws.Range(first - 2, 1, r, last));
            Center(ws.Range(first, 1, r, 1)); Center(ws.Range(first, 3, r, last));
            ws.Column(1).Width = 5; ws.Column(2).Width = 24; ws.Column(3).Width = 9; ws.Column(4).Width = 9;
            for (int c = 5; c <= last; c++) ws.Column(c).Width = 12;
            ws.Row(first - 1).Height = 30;
            ws.Range(first - 1, 5, first - 1, last).Style.Alignment.SetWrapText();
            ws.SheetView.FreezeRows(first - 1);
            ws.SheetView.FreezeColumns(2);
        }

        // ══ Helpers ════════════════════════════════════════════════════════════════

        /// <summary>Planning guide: voltage points on MintCard lines 1-8 in sequence, current on lines 9-10, then C cards.</summary>
        private static Dictionary<MaterialPositioning.SensorPoint, string> PlannedCards(MaterialPositioning.LocationResult plan, List<MaterialPositioning.SensorPoint> points)
        {
            var map = new Dictionary<MaterialPositioning.SensorPoint, string>();
            var volt = points.Where(p => p.Kind == MaterialPositioning.KindVoltage)
                             .OrderBy(p => p.IsAxleCounter ? 1 : 0).ToList();   // axle points last so their cards are the 9-10-blank ones
            int i = 0;
            foreach (var p in volt) { map[p] = $"MintCard-{i / MaterialPositioning.VoltagePinsPerCard + 1} / {i % MaterialPositioning.VoltagePinsPerCard + 1}"; i++; }
            int plainCards = plan.VoltageCards - plan.AxleVoltageCards;
            var slots = new List<string>();
            for (int c = 1; c <= plainCards; c++) { slots.Add($"MintCard-{c} / 9"); slots.Add($"MintCard-{c} / 10"); }
            int s = 0, cc = 0;
            foreach (var p in points.Where(p => p.Kind == MaterialPositioning.KindCurrent))
            {
                if (s < slots.Count) map[p] = slots[s++];
                else { map[p] = $"C-{cc / MaterialPositioning.PinsPerCard + 1} / {cc % MaterialPositioning.PinsPerCard + 1}"; cc++; }
            }
            return map;
        }

        private static int Title(IXLWorksheet ws, StationMaterialPositioning m, string subtitle, int lastCol)
        {
            var t = ws.Range(1, 1, 1, lastCol).Merge();
            Set(t.FirstCell(), $"MATERIAL POSITIONING REPORT  —  {m.StationName}{(string.IsNullOrWhiteSpace(m.StationCode) ? "" : $" ({m.StationCode})")}");
            t.Style.Font.SetBold().Font.SetFontSize(15).Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(Brand)
             .Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            ws.Row(1).Height = 26;
            var s = ws.Range(2, 1, 2, lastCol).Merge();
            Set(s.FirstCell(), $"{subtitle}     |     {m.ZoneDivision}     |     Generated {m.GeneratedAt:dd-MMM-yyyy HH:mm}     |     Prepared by {m.PreparedBy}     |     Spare {m.SparePercent:0.##} %");
            s.Style.Font.SetFontColor(Brand).Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#FFF4F1"));
            ws.Row(2).Height = 18;
            var accent = ws.Range(3, 1, 3, lastCol);
            accent.Style.Fill.SetBackgroundColor(Coral);
            ws.Row(3).Height = 4;
            return 5;
        }

        private static int Band(IXLWorksheet ws, int r, int from, int to, string text, XLColor color)
        {
            var b = ws.Range(r, from, r, to).Merge();
            Set(b.FirstCell(), text.ToUpperInvariant());
            b.Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(color);
            return r + 1;
        }

        private static int TwoLevelHeader(IXLWorksheet ws, int r, (string name, int from, int to, (XLColor dark, XLColor light) col)[] groups, string[] cols)
        {
            foreach (var g in groups)
            {
                var band = ws.Range(r, g.from, r, g.to);
                if (g.to > g.from) band.Merge();
                Set(band.FirstCell(), g.name.ToUpperInvariant());
                band.Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(g.col.dark)
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                for (int c = g.from; c <= g.to; c++) HeaderCell(ws.Cell(r + 1, c), cols[c - 1], g.col.light);
            }
            ws.Row(r).Height = 18;
            ws.Row(r + 1).Height = 30;
            return r + 2;
        }

        private static void HeaderCell(IXLCell cell, string text, XLColor fill)
        {
            Set(cell, text);
            cell.Style.Font.SetBold().Fill.SetBackgroundColor(fill)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center)
                .Alignment.SetWrapText();
        }

        private static IXLCell Set(IXLCell cell, object? value)
        {
            switch (value)
            {
                case null: cell.Value = ""; break;
                case int i: cell.Value = i; break;
                case decimal d: cell.Value = d; break;
                default: cell.Value = value.ToString(); break;
            }
            return cell;
        }

        private static void Status(IXLCell cell, string status)
        {
            var (fill, font) = status.ToLowerInvariant() switch
            {
                "ok" or "wired" => (OkFill, OkFont),
                "gap" or "missing" or "extra" => (GapFill, GapFont),
                "not wired" or "no" => (NoFill, NoFont),
                "wired only" => (InfoFill, InfoFont),
                _ => (XLColor.NoColor, XLColor.Black)
            };
            cell.Style.Fill.SetBackgroundColor(fill).Font.SetFontColor(font).Font.SetBold()
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        }

        private static void Compare(IXLCell cell, int wired, int planned) => Status(cell, wired == planned ? "ok" : "gap");

        private static void Grid(IXLRange range)
        {
            range.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin).Border.SetInsideBorderColor(XLColor.FromHtml("#A6A6A6"))
                 .Border.SetOutsideBorder(XLBorderStyleValues.Medium).Border.SetOutsideBorderColor(Brand);
        }

        private static void Box(IXLRange range)
        {
            range.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin).Border.SetInsideBorderColor(XLColor.FromHtml("#BFBFBF"))
                 .Border.SetOutsideBorder(XLBorderStyleValues.Medium).Border.SetOutsideBorderColor(Brand);
        }

        private static void TotalRow(IXLRange range)
        {
            range.Style.Font.SetBold().Fill.SetBackgroundColor(TotalFill)
                 .Border.SetTopBorder(XLBorderStyleValues.Double).Border.SetTopBorderColor(Brand);
        }

        private static void Center(IXLRange range) => range.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        private static void Widths(IXLWorksheet ws, params double[] widths)
        {
            for (int i = 0; i < widths.Length; i++) ws.Column(i + 1).Width = widths[i];
        }

        private static void Notes(IXLWorksheet ws, int r, IEnumerable<(string loc, string text)> notes, int lastCol)
        {
            var list = notes.ToList();
            if (list.Count == 0) return;
            r = Band(ws, r, 1, lastCol, "Remarks", GNote.dark);
            int top = r;
            foreach (var (loc, text) in list)
            {
                Set(ws.Cell(r, 2), loc).Style.Font.SetBold();
                var rg = ws.Range(r, 3, r, lastCol).Merge();
                Set(rg.FirstCell(), text);
                rg.Style.Alignment.SetWrapText();
                ws.Row(r).Height = Math.Max(15, 15 * Math.Ceiling(text.Length / 160.0));
                r++;
            }
            Grid(ws.Range(top, 1, r - 1, lastCol));
        }

        /// <summary>"A: Vr, TPR V (Loc)\nB: Charger V, Vf" — one line per asset, in sequence.</summary>
        private static string ByAsset(IEnumerable<(string asset, string title)> points) =>
            string.Join("\n", points.GroupBy(p => p.asset).Select(g => $"{g.Key}: {string.Join(", ", g.Select(x => x.title))}"));

        private static string SensorLabel(string key)
        {
            var p = key.Split('|');
            return (p[0] == MaterialPositioning.KindVoltage ? "V " : "C ") + (p.Length > 1 ? p[1] : "");
        }

        private static bool SameSet(List<string> a, List<string> b) =>
            a.Count == b.Count && !a.Except(b, StringComparer.OrdinalIgnoreCase).Any();
    }
}
