using Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Energy7.StationSurvey.Utility
{
    /// <summary>
    /// Material Positioning: survey gear (SiteSurvey rows) → sensor points (AttributeSensorMapping)
    /// → cards / A10 / ADC per location.
    ///
    /// Card rules (confirmed by Energy7):
    ///   Current card (C-n)            : 10 pins, all current.
    ///   Voltage card (AI = MintCard-n): pins 1-8 voltage only, pins 9-10 current only.
    ///   A voltage card carrying axle-counter points keeps pins 9-10 blank.
    ///   Every card has its own A10 (analog → digital) → 1 card = 1 A10 = 1 ADC.
    /// Which points share a card is decided at wiring time, so these are planning quantities.
    ///
    /// Survey reading follows e7mriv2web (API SiteSurveyService.GetLister):
    ///   TR column = relay end, TF column = feed end, signal aspect = last word of each entry,
    ///   point machine half / series from SiteSurvey/GetPM.
    /// Datalogger attributes are not in AttributeSensorMapping, so they are never counted.
    /// </summary>
    public static class MaterialPositioning
    {
        public const int PinsPerCard = 10;
        public const int VoltagePinsPerCard = 8;
        public const int CurrentPinsPerVoltageCard = 2;

        public const string KindVoltage = "Voltage";
        public const string KindCurrent = "Current";

        // Track: relay-end titles (TR column); every other TRACK attribute is feed end (TF column)
        private static readonly HashSet<string> TrackRelayEnd = new(StringComparer.OrdinalIgnoreCase)
            { "Vr", "Ir mA", "TPR V (Loc)", "TPR V" };

        public class SensorPoint
        {
            public string Asset { get; set; } = "";
            public string AssetType { get; set; } = "";
            public string Title { get; set; } = "";
            public string Kind { get; set; } = "";
            public string Type { get; set; } = "";
            public bool IsAxleCounter { get; set; }
            public int AssetTypeId { get; set; }
            public int Seq { get; set; }   // AttributeSensorMapping.Id → attribute order within an asset
        }

        // ── Sequencing used by the tab, report and Excel ──
        /// <summary>Track → Signal → Point machine → Axle counter → anything else.</summary>
        public static int TypeOrder(int assetTypeId) => assetTypeId switch { 1 => 1, 2 => 2, 3 => 3, 4 => 4, _ => 9 };
        public static int TypeOrder(string? assetTypeName) => (assetTypeName ?? "").Trim().ToUpperInvariant() switch
        {
            "TRACK" => 1, "SIGNAL" => 2, "POINT MACHINE" or "POINT_MACHINE" => 3, "AXLE COUNTER" or "AXLE_COUNTER" => 4, "" => 99, _ => 9
        };

        /// <summary>"PT-10" sorts after "PT-9": digits padded so text order = natural order.</summary>
        public static string NaturalKey(string? s) =>
            System.Text.RegularExpressions.Regex.Replace((s ?? "").Trim().ToUpperInvariant(), @"\d+", m => m.Value.PadLeft(8, '0'));

        /// <summary>Voltage before current; DC, AC, HZ; then by range ("Voltage|(0-150V)AC").</summary>
        public static (int, int, decimal, string) SensorOrder(string key)
        {
            var parts = key.Split('|');
            var kind = parts[0] == KindVoltage ? 0 : 1;
            var type = parts.Length > 1 ? parts[1].ToUpperInvariant() : "";
            int suffix = type.EndsWith("DC") ? 0 : type.EndsWith("AC") ? 1 : type.EndsWith("HZ") ? 2 : 3;
            var nums = System.Text.RegularExpressions.Regex.Matches(type, @"\d+(\.\d+)?").Select(m => decimal.Parse(m.Value)).ToList();
            return (kind, suffix, nums.Count > 0 ? nums.Max() : 0, type);
        }
        public static IEnumerable<string> OrderSensors(IEnumerable<string> keys) =>
            keys.Distinct().OrderBy(k => SensorOrder(k).Item1).ThenBy(k => SensorOrder(k).Item2).ThenBy(k => SensorOrder(k).Item3).ThenBy(k => k);

        public class LocationResult
        {
            public int SurveyId { get; set; }
            public int RfCluster { get; set; }
            public string Location { get; set; } = "";
            public string Side { get; set; } = "";
            public string DistanceFromRR { get; set; } = "";
            public List<SensorPoint> Points { get; set; } = new();
            public List<string> Warnings { get; set; } = new();

            public int Voltage { get; set; }
            public int Current { get; set; }
            public int AxleVoltage { get; set; }
            public int VoltageCards { get; set; }
            public int AxleVoltageCards { get; set; }
            public int CurrentOnVoltageCards { get; set; }
            public int CurrentCards { get; set; }
            public int A10 => VoltageCards + CurrentCards;
            public int Adc => A10;
            public int FtuChannels => A10 * PinsPerCard;   // e7mriv2web "FTU Type" = cards × 10
            public string TypeCard => string.Join(", ", new[]
            {
                CurrentCards > 0 ? $"C({CurrentCards})" : null,
                VoltageCards > 0 ? $"MintCard({VoltageCards})" : null
            }.Where(x => x != null));
            public int SpareVoltagePins { get; set; }
            public int SpareCurrentPins { get; set; }
            public int Conductor { get; set; }   // e7mriv2web formula: 2V + C + 2

            /// <summary>"Voltage|(0-150V)AC" → count</summary>
            public Dictionary<string, int> Sensors { get; set; } = new();
        }

        public static string SensorKey(string kind, string type) => $"{kind}|{(type ?? "").Trim()}";

        public static LocationResult Calculate(SiteSurvey row, List<AttributeSensorMapping> map, List<SurveyPointMachine> pointMachines)
        {
            var r = new LocationResult
            {
                SurveyId = row.Id,
                RfCluster = row.Cluster,
                Location = (row.LocationBoxNo ?? "").Trim(),
                Side = (row.LocationSide ?? "").Trim(),
                DistanceFromRR = (row.DistanceFromRR ?? "").Trim()
            };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // asset|title, never count twice

            void Add(string asset, AttributeSensorMapping m)
            {
                if (!seen.Add($"{asset}|{m.Title?.Trim()}")) return;
                r.Points.Add(new SensorPoint
                {
                    Asset = asset,
                    AssetType = m.AssetTypeName?.Trim() ?? "",
                    Title = m.Title?.Trim() ?? "",
                    Kind = m.Kind?.Trim() ?? "",
                    Type = m.Type?.Trim() ?? "",
                    IsAxleCounter = m.AssetTypeId == (int)Utility.AssetType.AXLE_COUNTER,
                    AssetTypeId = m.AssetTypeId,
                    Seq = m.Id
                });
            }

            List<AttributeSensorMapping> Of(Utility.AssetType t) => map.Where(m => m.AssetTypeId == (int)t).ToList();
            AttributeSensorMapping? Find(List<AttributeSensorMapping> list, string title) =>
                list.FirstOrDefault(m => string.Equals(m.Title?.Trim(), title, StringComparison.OrdinalIgnoreCase));

            // ── Track: TR = relay end, TF = feed end ──
            var track = Of(Utility.AssetType.TRACK);
            foreach (var t in Names(row.TR))
                foreach (var m in track.Where(m => TrackRelayEnd.Contains(m.Title?.Trim() ?? ""))) Add(t, m);
            foreach (var t in Names(row.TF))
                foreach (var m in track.Where(m => !TrackRelayEnd.Contains(m.Title?.Trim() ?? ""))) Add(t, m);

            // ── Point machine: A end always, B end only for a full (not half / series) machine ──
            var pm = Of(Utility.AssetType.POINT_MACHINE);
            foreach (var p in Names(row.PointMachine))
            {
                var info = pointMachines.FirstOrDefault(x => string.Equals(x.Name?.Trim(), p, StringComparison.OrdinalIgnoreCase));
                bool oneEnd = info != null && (info.IsHalfPointMachine || info.IsSeriesOpration);
                if (info == null) r.Warnings.Add($"Point {p}: half / full not set in survey point details — counted as full (A + B end).");
                foreach (var m in pm)
                {
                    var title = m.Title?.Trim() ?? "";
                    bool isB = title.StartsWith("B ", StringComparison.OrdinalIgnoreCase);
                    if (isB && oneEnd) continue;
                    Add(p, m);
                }
            }

            // ── Signal: "S21 HG" → lamp HG of S21 ──
            var sig = Of(Utility.AssetType.SIGNAL);
            foreach (var entry in Names(row.Signal))
            {
                var parts = entry.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    r.Warnings.Add($"Signal {entry}: no aspect given (write it as \"{entry} HG\") — not counted.");
                    continue;
                }
                var name = string.Join(" ", parts.Take(parts.Length - 1));
                var token = parts[^1].ToUpperInvariant();
                var titles = SignalTitles(name, token);
                if (titles.Count == 0)
                {
                    r.Warnings.Add($"Signal {entry}: aspect \"{token}\" not recognised — not counted.");
                    continue;
                }
                foreach (var title in titles)
                {
                    var m = Find(sig, title);
                    if (m != null) Add(name, m);
                }
            }

            // ── Axle counter: every mapped attribute ──
            var ac = Of(Utility.AssetType.AXLE_COUNTER);
            foreach (var a in Names(row.AxleCounter))
                foreach (var m in ac) Add(a, m);

            // ── Sequence: Track → Signal → Point → Axle counter, asset in natural order, attribute in mapping order ──
            r.Points = r.Points.OrderBy(p => TypeOrder(p.AssetTypeId)).ThenBy(p => NaturalKey(p.Asset)).ThenBy(p => p.Seq).ToList();

            // ── Totals and cards ──
            r.Voltage =r.Points.Count(p => p.Kind.Equals(KindVoltage, StringComparison.OrdinalIgnoreCase));
            r.Current = r.Points.Count(p => p.Kind.Equals(KindCurrent, StringComparison.OrdinalIgnoreCase));
            r.AxleVoltage = r.Points.Count(p => p.IsAxleCounter && p.Kind.Equals(KindVoltage, StringComparison.OrdinalIgnoreCase));
            foreach (var key in OrderSensors(r.Points.Select(p => SensorKey(p.Kind, p.Type))))
                r.Sensors[key] = r.Points.Count(p => SensorKey(p.Kind, p.Type) == key);

            var cards = Cards(r.Voltage, r.Current, r.AxleVoltage);
            r.VoltageCards = cards.voltageCards;
            r.AxleVoltageCards = cards.axleCards;
            r.CurrentOnVoltageCards = cards.currentOnVoltage;
            r.CurrentCards = cards.currentCards;
            r.SpareVoltagePins = cards.spareVoltage;
            r.SpareCurrentPins = cards.spareCurrent;
            r.Conductor = r.Points.Count == 0 ? 0 : (2 * r.Voltage) + r.Current + 2;
            return r;
        }

        /// <summary>Voltage cards = V ÷ 8; current first on pins 9-10 of voltage cards (not axle-counter cards), rest on current cards ÷ 10.</summary>
        public static (int voltageCards, int axleCards, int currentOnVoltage, int currentCards, int spareVoltage, int spareCurrent)
            Cards(int voltage, int current, int axleVoltage)
        {
            int vCards = CeilDiv(voltage, VoltagePinsPerCard);
            int axleCards = Math.Min(vCards, CeilDiv(axleVoltage, VoltagePinsPerCard));
            int slots = CurrentPinsPerVoltageCard * (vCards - axleCards);
            int onV = Math.Min(current, slots);
            int cCards = CeilDiv(current - onV, PinsPerCard);
            int spareV = vCards * VoltagePinsPerCard - voltage;
            int spareC = (slots - onV) + (cCards * PinsPerCard - (current - onV));
            return (vCards, axleCards, onV, cCards, spareV, spareC);
        }

        /// <summary>Attribute titles one signal entry needs. Shunt = signal name starting "SH" (or a PG lamp).</summary>
        public static List<string> SignalTitles(string name, string token)
        {
            bool shunt = name.Trim().StartsWith("SH", StringComparison.OrdinalIgnoreCase) || token == "PG";
            if (shunt)
            {
                var lamp = token switch { "RG" => "Off Aspect", "DG" => "On Aspect", "PG" => "PILOT", _ => null };
                return lamp == null ? new() : new() { $"{lamp} V", $"{lamp} mA", "Sh-HPR" };
            }
            switch (token)
            {
                case "RG": return new() { "RG V", "RG mA" };
                case "DG": return new() { "DG V", "DG mA", "DPR" };
                case "HG": return new() { "HG V", "HG mA", "HPR" };
                case "HHG": return new() { "HHG V", "HHG mA", "HHPR" };
                case "CO": case "COHG": case "CO_HG": case "C": return new() { "Co_Hg V", "Co_Hg mA", "Co_HPR" };
            }
            // Route indicator: UG / UGA / AUG → A … UGE / EUG → E (e7mriv2web also treats ASin as route)
            char? route = token switch
            {
                "UG" or "UGA" or "AUG" or "ASIN" => 'A',
                "UGB" or "BUG" => 'B',
                "UGC" or "CUG" => 'C',
                "UGD" or "DUG" => 'D',
                "UGE" or "EUG" => 'E',
                _ => null
            };
            return route == null ? new() : new() { $"{route}UG V", $"{route}UG mA", $"{route}UPR" };
        }

        /// <summary>"12T, 14T;16T" → [12T, 14T, 16T] (same split as the survey gear tab / e7mriv2web).</summary>
        public static List<string> Names(string? cell) =>
            (cell ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim().ToUpperInvariant())
                        .Where(x => x.Length > 0).Distinct().ToList();

        public static int CeilDiv(int a, int b) => a <= 0 ? 0 : (a + b - 1) / b;

        public static int WithSpare(int qty, decimal sparePercent) =>
            qty <= 0 || sparePercent <= 0 ? qty : qty + (int)Math.Ceiling(qty * sparePercent / 100m);

        public static string Label(StationLocation l) => l.HasSurvey ? $"cluster_{l.Plan.RfCluster}/{l.Plan.Location}" : l.ClusterName;

        /// <summary>Group planned (survey) and wired (Yard Config) points by asset name.</summary>
        public static List<AssetRow> Assets(List<StationLocation> locations)
        {
            var rows = new Dictionary<string, AssetRow>(StringComparer.OrdinalIgnoreCase);
            AssetRow Row(string asset, string type)
            {
                if (!rows.TryGetValue(asset, out var r)) rows[asset] = r = new AssetRow { Asset = asset, AssetType = type };
                if (string.IsNullOrEmpty(r.AssetType)) r.AssetType = type;
                return r;
            }
            foreach (var l in locations)
            {
                var label = Label(l);
                foreach (var p in l.Plan.Points)
                {
                    var r = Row(p.Asset, p.AssetType);
                    r.InSurvey = true;
                    if (!r.Locations.Contains(label)) r.Locations.Add(label);
                    if (p.Kind == KindVoltage) { r.Voltage++; r.VoltageInputs.Add(p.Title); }
                    else if (p.Kind == KindCurrent) { r.Current++; r.CurrentInputs.Add(p.Title); }
                    var key = SensorKey(p.Kind, p.Type);
                    r.Sensors[key] = r.Sensors.GetValueOrDefault(key) + 1;
                }
                foreach (var w in l.WiredPoints)
                {
                    var r = Row(w.Asset, w.AssetType);
                    if (!r.WiredLocations.Contains(label)) r.WiredLocations.Add(label);
                    if (w.Kind == KindVoltage) r.WiredVoltage++;
                    else if (w.Kind == KindCurrent) r.WiredCurrent++;
                }
            }
            foreach (var r in rows.Values)
                r.Sensors = OrderSensors(r.Sensors.Keys).ToDictionary(k => k, k => r.Sensors[k]);
            return rows.Values.OrderBy(r => TypeOrder(r.AssetType)).ThenBy(r => r.AssetType).ThenBy(r => NaturalKey(r.Asset)).ToList();
        }

        /// <summary>Station bill of material from all locations.</summary>
        public static List<BomLine> Bom(List<StationLocation> locations, decimal sparePercent)
        {
            var lines = new List<BomLine>();
            void Line(string group, string material, string unit, int calc, int? actual)
                => lines.Add(new BomLine
                {
                    No = lines.Count + 1, Group = group, Material = material, Unit = unit,
                    Calculated = calc, Spare = WithSpare(calc, sparePercent) - calc,
                    Required = WithSpare(calc, sparePercent), Actual = actual
                });

            Line("Card", "Voltage card (AI / MintCard)", "Pcs", locations.Sum(l => l.Plan.VoltageCards), locations.Sum(l => l.ActualVoltageCards));
            Line("Card", "Current card (C)", "Pcs", locations.Sum(l => l.Plan.CurrentCards), locations.Sum(l => l.ActualCurrentCards));
            Line("Card", "A10 unit", "Pcs", locations.Sum(l => l.Plan.A10), null);
            Line("Card", "ADC", "Pcs", locations.Sum(l => l.Plan.Adc), null);

            var sensorKeys = OrderSensors(locations.SelectMany(l => l.Plan.Sensors.Keys)).ToList();
            foreach (var key in sensorKeys)
            {
                var parts = key.Split('|');
                Line(parts[0] + " sensor", $"{parts[0]} sensor {parts[1]}", "Pcs",
                     locations.Sum(l => l.Plan.Sensors.TryGetValue(key, out var n) ? n : 0),
                     locations.Sum(l => l.ActualSensors.TryGetValue(key, out var n) ? n : 0));
            }
            Line("Cable", "Cable conductors (2V + C + 2)", "Cores", locations.Sum(l => l.Plan.Conductor), null);
            return lines;
        }
    }

    /// <summary>One line wired in Yard Config (CardLine), with its kind from the mapping or the line's ValueType.</summary>
    public class WiredPoint
    {
        public string Asset { get; set; } = "";
        public string AssetType { get; set; } = "";
        public string Title { get; set; } = "";
        public string Kind { get; set; } = "";      // Voltage | Current | DI
        public string Type { get; set; } = "";      // sensor range when the attribute is in AttributeSensorMapping
        public string Card { get; set; } = "";
        public int Line { get; set; }
        public bool InMapping { get; set; }         // false = datalogger / IPS / other attribute outside the mapping
    }

    /// <summary>Asset-wise material positioning: where each gear's points sit and what they need.</summary>
    public class AssetRow
    {
        public string Asset { get; set; } = "";
        public string AssetType { get; set; } = "";
        public List<string> Locations { get; set; } = new();
        public int Voltage { get; set; }
        public int Current { get; set; }
        public List<string> VoltageInputs { get; set; } = new();
        public List<string> CurrentInputs { get; set; } = new();
        public Dictionary<string, int> Sensors { get; set; } = new();
        public int WiredVoltage { get; set; }
        public int WiredCurrent { get; set; }
        public List<string> WiredLocations { get; set; } = new();
        public bool InSurvey { get; set; }
    }

    public class StationLocation
    {
        public MaterialPositioning.LocationResult Plan { get; set; } = new();
        public bool HasSurvey { get; set; } = true;   // false = Yard Config cluster with no survey gear row
        public string ClusterName { get; set; } = "";
        public List<WiredPoint> WiredPoints { get; set; } = new();
        public int WiredVoltageAll => WiredPoints.Count(p => p.Kind == MaterialPositioning.KindVoltage);
        public int WiredCurrentAll => WiredPoints.Count(p => p.Kind == MaterialPositioning.KindCurrent);
        public int WiredDI => WiredPoints.Count(p => p.Kind == "DI");
        public string WiredTypeCard { get; set; } = "";   // e.g. "C(1), MintCard(3)" — cards in use
        public int WiredFtuChannels { get; set; }          // cards in use × 10
        public int? ClusterId { get; set; }          // matching Yard Config cluster ("cluster_{RF}/{location}")
        public string LocationType { get; set; } = "";   // Indoor / Outdoor
        public string Power { get; set; } = "";
        public int ActualVoltage { get; set; }
        public int ActualCurrent { get; set; }
        public int ActualVoltageCards { get; set; }
        public int ActualCurrentCards { get; set; }
        public Dictionary<string, int> ActualSensors { get; set; } = new();
        public List<string> Gaps { get; set; } = new();
    }

    public class BomLine
    {
        public int No { get; set; }
        public string Group { get; set; } = "";
        public string Material { get; set; } = "";
        public string Unit { get; set; } = "";
        public int Calculated { get; set; }
        public int Spare { get; set; }
        public int Required { get; set; }
        public int? Actual { get; set; }   // wired in Yard Config (null = not tracked there)
    }

    public class StationMaterialPositioning
    {
        public int SiteId { get; set; }
        public string StationName { get; set; } = "";
        public string StationCode { get; set; } = "";
        public string ZoneDivision { get; set; } = "";
        public string PreparedBy { get; set; } = "";
        public DateTime GeneratedAt { get; set; }
        public decimal SparePercent { get; set; }
        public List<StationLocation> Locations { get; set; } = new();
        public List<BomLine> Bom { get; set; } = new();
        public List<AssetRow> Assets { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public int MappingRows { get; set; }
    }
}
