// File: Domain/Dto/NetworkReportRowVM.cs

using System.Collections.Generic;

namespace Domain.Dto
{
    public class NetworkReportRowVM
    {
        public string ZoneName     { get; set; }
        public string DivisionName { get; set; }
        public string SiteName     { get; set; }
        public string ClusterName  { get; set; }
        public string ModemName    { get; set; }
        public int?   Version      { get; set; }
        public string SimNumber    { get; set; }
        public string SimType      { get; set; }
        public decimal? SignalStrength { get; set; }
        public bool TcpStatus      { get; set; }
        public bool MqttStatus     { get; set; }
        public bool IsOnline       { get { return TcpStatus || MqttStatus; } }
        public string ModemUpTime  { get; set; }
        public string ModemDownTime{ get; set; }
        public List<NetworkReportA10VM> A10List { get; set; }
    }

    public class NetworkReportA10VM
    {
        public int    A10Id           { get; set; }
        public string FirmwareVersion { get; set; }
        public string ADCTypeName     { get; set; }
        public bool   TcpStatus       { get; set; }
        public bool   MqttStatus      { get; set; }
        public string UpTime          { get; set; }
        public string DownTime        { get; set; }
    }
}
