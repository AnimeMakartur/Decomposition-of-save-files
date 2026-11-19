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

    public class RoadEdgeDto
    {
        public int Id { get; set; }
        public string PrefabType { get; set; }
        public int StartNodeId { get; set; }
        public int EndNodeId { get; set; }
        public float Length { get; set; }
    }

    public class BuildingDto
    {
        public int Id { get; set; }
        public string PrefabType { get; set; }
        public int ConnectedRoadEdgeId { get; set; }
        public float[] Pos { get; set; }
    }

    public class TransportStopDto
    {
        public int Id { get; set; }
        public string PrefabType { get; set; }
        public int ParentEntityId { get; set; } // Дорога або будівля станції
        public float[] Pos { get; set; }
    }

    public class TransitRouteDto
    {
        public int Id { get; set; }
        public string PrefabType { get; set; }
        public List<int> WaypointEntityIds { get; set; } = new List<int>();
    }
}