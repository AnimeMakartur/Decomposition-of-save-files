using System.Collections.Generic;

namespace Cs2DataExporter
{
    public class CityGraphExport
    {
        public List<RoadNodeDto> RoadNodes { get; set; } = new List<RoadNodeDto>();
        public List<RoadEdgeDto> RoadEdges { get; set; } = new List<RoadEdgeDto>();
        public List<BuildingDto> Buildings { get; set; } = new List<BuildingDto>();
        public List<TransportStopDto> Stops { get; set; } = new List<TransportStopDto>();
        public List<TransitRouteDto> Routes { get; set; } = new List<TransitRouteDto>();
    }

    public class RoadNodeDto
    {
        public int Id { get; set; }
        public float[] Pos { get; set; } // [X, Y, Z]
    }

    public class RoadLaneDto
    {
        public int Id { get; set; }
        public string Type { get; set; } // "Car", "Bus", "Tram", "Bicycle", "Pedestrian", "Parking", "Other"
        public int Direction { get; set; } // 1 (Start -> End), -1 (End -> Start), 0 (двосторонній рух, напр. тротуар)
    }

    public class RoadEdgeDto
    {
        public int Id { get; set; }
        public string InGameName { get; set; }
        public string PrefabType { get; set; }
        public int StartNodeId { get; set; }
        public int EndNodeId { get; set; }
        public float Length { get; set; }
        public List<RoadLaneDto> Lanes { get; set; } = new List<RoadLaneDto>();
    }

    public class BuildingDto
    {
        public int Id { get; set; }
        public string InGameName { get; set; }
        public string PrefabType { get; set; }
        public int ConnectedRoadEdgeId { get; set; }
        public float[] Pos { get; set; }
    }

    public class TransportStopDto
    {
        public int Id { get; set; }
        public string InGameName { get; set; }
        public string PrefabType { get; set; }
        public string TransportType { get; set; }
        public int ParentEntityId { get; set; } // Дорога або будівля станції
        public int? RoadEdgeId { get; set; }
        public float[] Pos { get; set; }
    }

    public class TransitRouteDto
    {
        public int Id { get; set; }
        public string InGameName { get; set; }
        public string PrefabType { get; set; }
        public string TransportType { get; set; }
        public int? StartStopId { get; set; }
        public int? EndStopId { get; set; }
        public int? TicketPrice { get; set; }
        public int? VehicleCount { get; set; }
        public int? TotalPassengerCapacity { get; set; }
        public List<int> WaypointEntityIds { get; set; } = new List<int>();
        public List<int> StopIds { get; set; } = new List<int>();
        public List<int> RoadEdgeIds { get; set; } = new List<int>();
        public List<TransitVehicleDto> Vehicles { get; set; } = new List<TransitVehicleDto>();
    }

    public class TransitVehicleDto
    {
        public int Id { get; set; }
        public string PrefabType { get; set; }
        public int? PassengerCapacity { get; set; }
    }
}