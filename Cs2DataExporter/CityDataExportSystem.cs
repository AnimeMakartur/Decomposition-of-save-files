using System;
using System.Collections.Generic;
using System.IO;
using Colossal.Serialization.Entities;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Routes;
using Game.Tools;
using Game.UI;
using Newtonsoft.Json;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Cs2DataExporter
{
    public partial class CityDataExportSystem : GameSystemBase
    {
        private PrefabSystem _prefabSystem;
        private NameSystem _nameSystem;

        private EntityQuery _roadNodeQuery;
        private EntityQuery _roadEdgeQuery;
        private EntityQuery _buildingQuery;
        private EntityQuery _stopQuery;
        private EntityQuery _routeQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            _nameSystem = World.GetOrCreateSystemManaged<NameSystem>();

            // 1. Вузли доріг (перехрестя/точки з'єднання)
            _roadNodeQuery = GetEntityQuery(
                ComponentType.ReadOnly<Node>(),
                ComponentType.ReadOnly<Road>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>()
            );

            // 2. Сегменти доріг (ребра графа)
            _roadEdgeQuery = GetEntityQuery(
                ComponentType.ReadOnly<Edge>(),
                ComponentType.ReadOnly<Road>(),
                ComponentType.ReadOnly<Curve>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>()
            );

            // 3. Будівлі (включно з парками, школами, житлом)
            _buildingQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<Transform>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>()
            );

            // 4. Зупинки громадського транспорту
            _stopQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Routes.TransportStop>(),
                ComponentType.ReadOnly<Transform>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>()
            );

            // 5. Лінії маршрутів громадського транспорту
            _routeQuery = GetEntityQuery(
                ComponentType.ReadOnly<TransportLine>(),
                ComponentType.ReadOnly<RouteWaypoint>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>()
            );

            Enabled = false;
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            if (mode == GameMode.Game)
            {
                ExportCityToAiJson();
            }
        }

        protected override void OnUpdate() { }

        public void ExportCityToAiJson()
        {
            try
            {
                Mod.log.Info("Початок експорту графа міста для ШІ...");
                var export = new CityGraphExport();
                var laneToRoadEdgeId = new Dictionary<int, int>();

                // 1. Збір вузлів доріг
                using (var entities = _roadNodeQuery.ToEntityArray(Allocator.Temp))
                {
                    foreach (var entity in entities)
                    {
                        var node = EntityManager.GetComponentData<Node>(entity);
                        export.RoadNodes.Add(new RoadNodeDto
                        {
                            Id = entity.Index,
                            Pos = RoundPos(node.m_Position)
                        });
                    }
                }

                // 2. Збір ребер доріг, їхніх назв та масиву смуг
                using (var entities = _roadEdgeQuery.ToEntityArray(Allocator.Temp))
                {
                    foreach (var entity in entities)
                    {
                        var edge = EntityManager.GetComponentData<Edge>(entity);
                        var curve = EntityManager.GetComponentData<Curve>(entity);
                        var prefabRef = EntityManager.GetComponentData<PrefabRef>(entity);

                        export.RoadEdges.Add(new RoadEdgeDto
                        {
                            Id = entity.Index,
                            InGameName = GetEntityInGameName(entity),
                            PrefabType = GetPrefabName(prefabRef.m_Prefab),
                            StartNodeId = edge.m_Start.Index,
                            EndNodeId = edge.m_End.Index,
                            Length = (float)Math.Round(curve.m_Length, 2),
                            Lanes = ExtractRoadLanes(entity, laneToRoadEdgeId)
                        });
                    }
                }

                // 3. Збір будівель
                using (var entities = _buildingQuery.ToEntityArray(Allocator.Temp))
                {
                    foreach (var entity in entities)
                    {
                        var building = EntityManager.GetComponentData<Building>(entity);
                        var transform = EntityManager.GetComponentData<Transform>(entity);
                        var prefabRef = EntityManager.GetComponentData<PrefabRef>(entity);

                        export.Buildings.Add(new BuildingDto
                        {
                            Id = entity.Index,
                            InGameName = GetEntityInGameName(entity),
                            PrefabType = GetPrefabName(prefabRef.m_Prefab),
                            ConnectedRoadEdgeId = building.m_RoadEdge.Index,
                            Pos = RoundPos(transform.m_Position)
                        });
                    }
                }

                // 4. Збір зупинок транспорту
                using (var entities = _stopQuery.ToEntityArray(Allocator.Temp))
                {
                    foreach (var entity in entities)
                    {
                        var transform = EntityManager.GetComponentData<Transform>(entity);
                        var prefabRef = EntityManager.GetComponentData<PrefabRef>(entity);
                        int parentId = -1;

                        if (EntityManager.HasComponent<Owner>(entity))
                        {
                            parentId = EntityManager.GetComponentData<Owner>(entity).m_Owner.Index;
                        }
                        else if (EntityManager.HasComponent<Attached>(entity))
                        {
                            parentId = EntityManager.GetComponentData<Attached>(entity).m_Parent.Index;
                        }

                        export.Stops.Add(new TransportStopDto
                        {
                            Id = entity.Index,
                            InGameName = GetEntityInGameName(entity),
                            PrefabType = GetPrefabName(prefabRef.m_Prefab),
                            TransportType = GetStopTransportType(prefabRef.m_Prefab),
                            ParentEntityId = parentId,
                            RoadEdgeId = GetStopRoadEdgeId(entity, laneToRoadEdgeId),
                            Pos = RoundPos(transform.m_Position)
                        });
                    }
                }

                // 5. Збір маршрутів та їхніх точок (Waypoints)
                var stopIds = new HashSet<int>();
                foreach (var stop in export.Stops)
                {
                    stopIds.Add(stop.Id);
                }

                using (var entities = _routeQuery.ToEntityArray(Allocator.Temp))
                {
                    foreach (var entity in entities)
                    {
                        var prefabRef = EntityManager.GetComponentData<PrefabRef>(entity);
                        var waypointsBuffer = EntityManager.GetBuffer<RouteWaypoint>(entity);
                        var line = EntityManager.GetComponentData<TransportLine>(entity);

                        var routeDto = new TransitRouteDto
                        {
                            Id = entity.Index,
                            InGameName = GetEntityInGameName(entity),
                            PrefabType = GetPrefabName(prefabRef.m_Prefab),
                            TransportType = GetRouteTransportType(prefabRef.m_Prefab),
                            TicketPrice = line.m_TicketPrice
                        };

                        for (int i = 0; i < waypointsBuffer.Length; i++)
                        {
                            var wpEntity = waypointsBuffer[i].m_Waypoint;
                            if (EntityManager.HasComponent<Connected>(wpEntity))
                            {
                                var connected = EntityManager.GetComponentData<Connected>(wpEntity);
                                routeDto.WaypointEntityIds.Add(connected.m_Connected.Index);
                                if (stopIds.Contains(connected.m_Connected.Index))
                                {
                                    routeDto.StopIds.Add(connected.m_Connected.Index);
                                }
                            }
                            else
                            {
                                routeDto.WaypointEntityIds.Add(wpEntity.Index);
                                if (stopIds.Contains(wpEntity.Index))
                                {
                                    routeDto.StopIds.Add(wpEntity.Index);
                                }
                            }
                        }

                        if (routeDto.StopIds.Count > 0)
                        {
                            routeDto.StartStopId = routeDto.StopIds[0];
                            routeDto.EndStopId = routeDto.StopIds[routeDto.StopIds.Count - 1];
                        }

                        routeDto.RoadEdgeIds = ExtractRouteRoadEdgeIds(entity, laneToRoadEdgeId);
                        routeDto.Vehicles = ExtractRouteVehicles(entity, out int? totalPassengerCapacity);
                        routeDto.VehicleCount = routeDto.Vehicles.Count;
                        routeDto.TotalPassengerCapacity = totalPassengerCapacity;
                        export.Routes.Add(routeDto);
                    }
                }

                // Запис у JSON файл у папці AppData гри
                string outputDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"..\LocalLow\Colossal Order\Cities Skylines II\AI_Exports"
                );
                Directory.CreateDirectory(outputDir);

                string filePath = Path.Combine(outputDir, $"city_graph_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                string json = JsonConvert.SerializeObject(export, Formatting.Indented);
                File.WriteAllText(filePath, json);

                Mod.log.Info($"Експорт успішно завершено! Файл збережено: {filePath}");
            }
            catch (Exception ex)
            {
                Mod.log.Error(ex, "Помилка під час експорту даних міста.");
            }
        }

        /// <summary>
        /// Отримує список усіх смуг (SubLane), що належать сегменту дороги, із їх типом та напрямком.
        /// </summary>
        private List<RoadLaneDto> ExtractRoadLanes(
            Entity edgeEntity,
            Dictionary<int, int> laneToRoadEdgeId)
        {
            var lanesList = new List<RoadLaneDto>();

            if (!EntityManager.HasBuffer<Game.Net.SubLane>(edgeEntity))
            {
                return lanesList;
            }

            var subLanes = EntityManager.GetBuffer<Game.Net.SubLane>(edgeEntity);
            for (int i = 0; i < subLanes.Length; i++)
            {
                Entity laneEntity = subLanes[i].m_SubLane;
                if (!EntityManager.Exists(laneEntity) || !EntityManager.HasComponent<Lane>(laneEntity))
                {
                    continue;
                }

                int direction = 1;
                string laneType = DetermineLaneType(laneEntity, ref direction);

                // Ігноруємо внутрішні технічні/допоміжні смуги без фізичного типу, якщо потрібно
                if (laneType == "Ignore")
                {
                    continue;
                }

                laneToRoadEdgeId[laneEntity.Index] = edgeEntity.Index;
                lanesList.Add(new RoadLaneDto
                {
                    Id = laneEntity.Index,
                    Type = laneType,
                    Direction = direction
                });
            }

            return lanesList;
        }

        private int? GetStopRoadEdgeId(Entity stopEntity, Dictionary<int, int> laneToRoadEdgeId)
        {
            if (!EntityManager.HasComponent<Game.Routes.AccessLane>(stopEntity))
            {
                return null;
            }

            var accessLane = EntityManager.GetComponentData<Game.Routes.AccessLane>(stopEntity);
            return accessLane.m_Lane != Entity.Null &&
                laneToRoadEdgeId.TryGetValue(accessLane.m_Lane.Index, out int roadEdgeId)
                ? (int?)roadEdgeId
                : null;
        }

        private List<int> ExtractRouteRoadEdgeIds(
            Entity routeEntity,
            Dictionary<int, int> laneToRoadEdgeId)
        {
            var roadEdgeIds = new List<int>();
            var uniqueRoadEdgeIds = new HashSet<int>();

            if (!EntityManager.HasBuffer<Game.Routes.RouteSegment>(routeEntity))
            {
                return roadEdgeIds;
            }

            var routeSegments = EntityManager.GetBuffer<Game.Routes.RouteSegment>(routeEntity);
            for (int i = 0; i < routeSegments.Length; i++)
            {
                Entity segmentEntity = routeSegments[i].m_Segment;
                if (!EntityManager.Exists(segmentEntity) ||
                    !EntityManager.HasComponent<Game.Routes.RouteLane>(segmentEntity))
                {
                    continue;
                }

                var routeLane = EntityManager.GetComponentData<Game.Routes.RouteLane>(segmentEntity);
                AddRoadEdgeId(routeLane.m_StartLane, laneToRoadEdgeId, uniqueRoadEdgeIds, roadEdgeIds);
                AddRoadEdgeId(routeLane.m_EndLane, laneToRoadEdgeId, uniqueRoadEdgeIds, roadEdgeIds);
            }

            return roadEdgeIds;
        }

        private static void AddRoadEdgeId(
            Entity laneEntity,
            Dictionary<int, int> laneToRoadEdgeId,
            HashSet<int> uniqueRoadEdgeIds,
            List<int> roadEdgeIds)
        {
            if (laneEntity != Entity.Null &&
                laneToRoadEdgeId.TryGetValue(laneEntity.Index, out int roadEdgeId) &&
                uniqueRoadEdgeIds.Add(roadEdgeId))
            {
                roadEdgeIds.Add(roadEdgeId);
            }
        }

        private List<TransitVehicleDto> ExtractRouteVehicles(
            Entity routeEntity,
            out int? totalPassengerCapacity)
        {
            var vehicles = new List<TransitVehicleDto>();
            var capacitySum = 0;
            var allCapacitiesKnown = true;

            if (EntityManager.HasBuffer<Game.Routes.RouteVehicle>(routeEntity))
            {
                var routeVehicles = EntityManager.GetBuffer<Game.Routes.RouteVehicle>(routeEntity);
                for (int i = 0; i < routeVehicles.Length; i++)
                {
                    Entity vehicleEntity = routeVehicles[i].m_Vehicle;
                    if (!EntityManager.Exists(vehicleEntity))
                    {
                        continue;
                    }

                    Entity prefabEntity = Entity.Null;
                    if (EntityManager.HasComponent<PrefabRef>(vehicleEntity))
                    {
                        prefabEntity = EntityManager.GetComponentData<PrefabRef>(vehicleEntity).m_Prefab;
                    }

                    int? passengerCapacity = GetVehiclePassengerCapacity(prefabEntity);
                    if (passengerCapacity.HasValue)
                    {
                        capacitySum += passengerCapacity.Value;
                    }
                    else
                    {
                        allCapacitiesKnown = false;
                    }

                    vehicles.Add(new TransitVehicleDto
                    {
                        Id = vehicleEntity.Index,
                        PrefabType = GetPrefabName(prefabEntity),
                        PassengerCapacity = passengerCapacity
                    });
                }
            }

            totalPassengerCapacity = vehicles.Count > 0 && allCapacitiesKnown
                ? (int?)capacitySum
                : null;
            return vehicles;
        }

        private int? GetVehiclePassengerCapacity(Entity prefabEntity)
        {
            if (prefabEntity == Entity.Null)
            {
                return null;
            }

            if (EntityManager.HasComponent<Game.Prefabs.PublicTransportVehicleData>(prefabEntity))
            {
                return EntityManager.GetComponentData<Game.Prefabs.PublicTransportVehicleData>(prefabEntity)
                    .m_PassengerCapacity;
            }

            return null;
        }

        private string GetStopTransportType(Entity prefabEntity)
        {
            if (prefabEntity != Entity.Null &&
                EntityManager.HasComponent<Game.Prefabs.TransportStopData>(prefabEntity))
            {
                return EntityManager.GetComponentData<Game.Prefabs.TransportStopData>(prefabEntity)
                    .m_TransportType.ToString();
            }

            return "Unknown";
        }

        private string GetRouteTransportType(Entity prefabEntity)
        {
            if (prefabEntity != Entity.Null &&
                EntityManager.HasComponent<Game.Prefabs.TransportLineData>(prefabEntity))
            {
                return EntityManager.GetComponentData<Game.Prefabs.TransportLineData>(prefabEntity)
                    .m_TransportType.ToString();
            }

            return "Unknown";
        }

        /// <summary>
        /// Визначає функціональний тип смуги за її ECS-компонентами та префабом.
        /// </summary>
        private string DetermineLaneType(Entity laneEntity, ref int direction)
        {
            // 1. Паркувальні смуги (можна відфільтрувати або залишити окремим типом)
            if (EntityManager.HasComponent<Game.Net.ParkingLane>(laneEntity))
            {
                var parkingLane = EntityManager.GetComponentData<Game.Net.ParkingLane>(laneEntity);
                direction = (parkingLane.m_Flags & ParkingLaneFlags.Invert) != 0 ? -1 : 1;
                return "Parking";
            }

            // 2. Пішохідна смуга (тротуар / перехід)
            if (EntityManager.HasComponent<Game.Net.PedestrianLane>(laneEntity))
            {
                direction = 0;
                return "Pedestrian";
            }

            // 3. Рейковий транспорт (трамвайні колії на дорозі)
            if (EntityManager.HasComponent<Game.Net.TrackLane>(laneEntity))
            {
                var trackLane = EntityManager.GetComponentData<Game.Net.TrackLane>(laneEntity);
                direction = (trackLane.m_Flags & TrackLaneFlags.Twoway) != 0
                    ? 0
                    : (trackLane.m_Flags & TrackLaneFlags.Invert) != 0 ? -1 : 1;
                return "Tram";
            }

            // 4. Автомобільні, автобусні та велосипедні смуги
            if (EntityManager.HasComponent<Game.Net.CarLane>(laneEntity))
            {
                var carLane = EntityManager.GetComponentData<Game.Net.CarLane>(laneEntity);
                direction = (carLane.m_Flags & CarLaneFlags.Twoway) != 0
                    ? 0
                    : (carLane.m_Flags & CarLaneFlags.Invert) != 0 ? -1 : 1;

                // Перевірка на велосипедну смугу через префаб CarLaneData
                if (EntityManager.HasComponent<PrefabRef>(laneEntity))
                {
                    var prefabEntity = EntityManager.GetComponentData<PrefabRef>(laneEntity).m_Prefab;
                    if (EntityManager.HasComponent<CarLaneData>(prefabEntity))
                    {
                        var carLaneData = EntityManager.GetComponentData<CarLaneData>(prefabEntity);
                        if ((carLaneData.m_RoadTypes & RoadTypes.Bicycle) != 0 &&
                            (carLaneData.m_RoadTypes & RoadTypes.Car) == 0)
                        {
                            return "Bicycle";
                        }
                    }
                }

                // Смуга громадського транспорту (виділена автобусна смуга)
                if ((carLane.m_Flags & CarLaneFlags.PublicOnly) != 0)
                {
                    return "Bus";
                }

                return "Car";
            }

            return "Ignore";
        }

        /// <summary>
        /// Повертає локалізовану або кастомну назву об'єкта, яку бачить гравець у UI.
        /// </summary>
        private string GetEntityInGameName(Entity entity)
        {
            if (entity == Entity.Null || _nameSystem == null)
            {
                return string.Empty;
            }

            if (_nameSystem.TryGetCustomName(entity, out string customName) &&
                !string.IsNullOrEmpty(customName))
            {
                return customName;
            }

            return _nameSystem.GetRenderedLabelName(entity) ?? string.Empty;
        }

        private string GetPrefabName(Entity prefabEntity)
        {
            if (prefabEntity != Entity.Null && _prefabSystem.TryGetPrefab<PrefabBase>(prefabEntity, out var prefab))
            {
                return prefab.name;
            }
            return "Unknown";
        }

        private float[] RoundPos(float3 pos)
        {
            return new float[]
            {
                (float)Math.Round(pos.x, 1),
                (float)Math.Round(pos.y, 1),
                (float)Math.Round(pos.z, 1)
            };
        }
    }
}