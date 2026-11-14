namespace Domain
{
    /// <summary>API AssetAttribute/GetSensorMapping row: which sensor (Kind + range Type) an attribute needs.</summary>
    public class AttributeSensorMapping
    {
        public int Id { get; set; }
        public int AssetTypeId { get; set; }
        public int AssetAttributeId { get; set; }
        public string Kind { get; set; }      // Voltage | Current
        public string Type { get; set; }      // e.g. (0-150V)AC
        //Extra
        public string AssetTypeName { get; set; }
        public string Title { get; set; }
        public string AliasName { get; set; }
    }
}
