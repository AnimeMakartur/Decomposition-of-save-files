using System;
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
using Newtonsoft.Json;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Cs2DataExporter
{
    public partial class CityDataExportSystem : GameSystemBase
    {
        private PrefabSystem _prefabSystem;
        private EntityQuery _roadNodeQuery;
        private EntityQuery _roadEdgeQuery;
        private EntityQuery _buildingQuery;
        private EntityQuery _stopQuery;
        private EntityQuery _routeQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();

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

            Enabled = false; // Виконуємо лише за тригером завантаження збереження
        }

        // Автоматичний запуск після завантаження файлу збереження (.cok)
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

                // 2. Збір ребер доріг (з'єднують StartNode та EndNode)
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
                            PrefabType = GetPrefabName(prefabRef.m_Prefab),
                            StartNodeId = edge.m_Start.Index,
                            EndNodeId = edge.m_End.Index,
                            Length = (float)Math.Round(curve.m_Length, 2)
                        });
                    }
                }

                // 3. Збір будівель та їх прив'язки до конкретного сегмента дороги
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
                            PrefabType = GetPrefabName(prefabRef.m_Prefab),
                            ParentEntityId = parentId,
                            Pos = RoundPos(transform.m_Position)
                        });
                    }
                }

                // 5. Збір маршрутів та їхніх точок (Waypoints)
                using (var entities = _routeQuery.ToEntityArray(Allocator.Temp))
                {
                    foreach (var entity in entities)
                    {
                        var prefabRef = EntityManager.GetComponentData<PrefabRef>(entity);
                        var waypointsBuffer = EntityManager.GetBuffer<RouteWaypoint>(entity);

                        var routeDto = new TransitRouteDto
                        {
                            Id = entity.Index,
                            PrefabType = GetPrefabName(prefabRef.m_Prefab)
                        };

                        for (int i = 0; i < waypointsBuffer.Length; i++)
                        {
                            var wpEntity = waypointsBuffer[i].m_Waypoint;
                            // Якщо точка маршруту прив'язана до зупинки (Connected), беремо ID самої зупинки
                            if (EntityManager.HasComponent<Connected>(wpEntity))
                            {
                                var connected = EntityManager.GetComponentData<Connected>(wpEntity);
                                routeDto.WaypointEntityIds.Add(connected.m_Connected.Index);
                            }
                            else
                            {
                                routeDto.WaypointEntityIds.Add(wpEntity.Index);
                            }
                        }

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