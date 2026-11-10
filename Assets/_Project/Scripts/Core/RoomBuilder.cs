using System;
using PaletteShaper.Gimmicks;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace PaletteShaper.Core
{
    public sealed class BuiltRoom
    {
        public GameObject Root;
        public Vector2 SpawnPoint;
        public RoomExit Exit;
        public Rect Bounds;
    }

    /// <summary>
    /// 20x11 ASCII 맵 → 씬 오브젝트.
    /// 좌표: 방 좌하단 타일 모서리 = (0,0). 맵의 row 0(맨 위)은 y = 10 ~ 11 칸.
    /// - '#': Terrain_Base 타일맵 (TilemapCollider2D Merge + Static Rigidbody2D + CompositeCollider2D Polygons/Synchronous)
    ///        오른쪽/아래가 빈 칸이면 외곽선 오버레이 타일 추가
    /// - 'C','W': Crimson_Solid 타일맵 (같은 Composite 구성) + PaletteObject 타일 교체
    /// - 'X' 가시, 'B'/'R' 탄성, 'G'+'=' 톱니 발판·레일, '*' 코어, 'E' 석문(우측 하단 기준 2x3), 'P' 스폰
    /// - 좌/우/상단 보이지 않는 경계 벽, 빈 공간 배경은 BG_Wall(Warm Gray)
    /// </summary>
    public static class RoomBuilder
    {
        const int W = GameConstants.ROOM_WIDTH_TILES;
        const int H = GameConstants.ROOM_HEIGHT_TILES;

        public static Vector3Int CellToTile(int col, int row) => new Vector3Int(col, H - 1 - row, 0);
        public static Vector2 CellCenter(int col, int row) => new Vector2(col + 0.5f, H - 1 - row + 0.5f);
        public static float CellBottom(int row) => H - 1 - row;

        public static void Validate(string[] map)
        {
            if (map == null || map.Length != H)
                throw new ArgumentException($"맵 높이는 {H}줄이어야 합니다 (현재 {map?.Length ?? 0}).");
            for (int r = 0; r < H; r++)
                if (map[r] == null || map[r].Length != W)
                    throw new ArgumentException($"맵 {r}번째 줄 길이는 {W}이어야 합니다 (현재 {map[r]?.Length ?? 0}).");
        }

        public static BuiltRoom Build(string[] map, PaletteShaperAssets a, Transform parent, string name = "Room", int seed = 0)
        {
            Validate(map);
            if (a == null) throw new ArgumentNullException(nameof(a));

            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            var built = new BuiltRoom { Root = root, Bounds = new Rect(0, 0, W, H), SpawnPoint = new Vector2(1.5f, 1f) };

            // 타일맵
            var grid = NewChild(root.transform, "Grid", Vector2.zero);
            grid.AddComponent<Grid>();

            // 배경 (Dark Ruins): Deep Charcoal 벽돌 + 약 18% 확률로 금/점/덩굴 변종. seed로 Room마다 고정된 배치
            var background = CreateTilemap(grid.transform, "Tilemap_BG", 0,
                GameConstants.SORT_BG_WALL, 0, false, out _, out _, out _);
            var rng = new System.Random(seed * 7919 + 17);
            for (int r = 0; r < H; r++)
            {
                for (int c = 0; c < W; c++)
                {
                    int variant = 0;
                    if (a.bgWallTiles != null && a.bgWallTiles.Length > 1 && rng.NextDouble() < GameConstants.BG_VARIANT_CHANCE)
                        variant = 1 + rng.Next(a.bgWallTiles.Length - 1);
                    if (a.bgWallTiles != null && a.bgWallTiles.Length > 0)
                        background.SetTile(CellToTile(c, r), a.bgWallTiles[variant]);
                }
            }
            var terrain = CreateTilemap(grid.transform, "Tilemap_Terrain", GameConstants.LAYER_TERRAIN_BASE,
                GameConstants.SORT_TERRAIN_BASE, 0, true, out _, out var terrainCollider, out var terrainComposite);
            var terrainEdge = CreateTilemap(grid.transform, "Tilemap_TerrainEdge", GameConstants.LAYER_TERRAIN_BASE,
                GameConstants.SORT_TERRAIN_BASE, 1, false, out _, out _, out _);
            var crimson = CreateTilemap(grid.transform, "Tilemap_Crimson", GameConstants.LAYER_CRIMSON_SOLID,
                GameConstants.SORT_GIMMICK_ACTIVE, 0, true, out var crimsonRenderer, out var crimsonCollider, out var crimsonComposite);

            var gimmicks = NewChild(root.transform, "Gimmicks", Vector2.zero).transform;
            bool crimsonUsed = false;

            for (int r = 0; r < H; r++)
            {
                for (int c = 0; c < W; c++)
                {
                    char ch = map[r][c];
                    Vector2 center = CellCenter(c, r);
                    switch (ch)
                    {
                        case GameConstants.SYM_STONE:
                            terrain.SetTile(CellToTile(c, r), a.tileBaseStone);
                            bool right = !IsStone(map, c + 1, r);
                            bool bottom = !IsStone(map, c, r + 1);
                            if (right && bottom) terrainEdge.SetTile(CellToTile(c, r), a.tileBaseStoneEdgeBoth);
                            else if (right) terrainEdge.SetTile(CellToTile(c, r), a.tileBaseStoneEdgeRight);
                            else if (bottom) terrainEdge.SetTile(CellToTile(c, r), a.tileBaseStoneEdgeBottom);
                            break;

                        case GameConstants.SYM_CRIMSON_BLOCK:
                        case GameConstants.SYM_CRIMSON_WALL:
                            crimson.SetTile(CellToTile(c, r), a.tileCrimsonSolid);
                            crimsonUsed = true;
                            break;

                        case GameConstants.SYM_CRIMSON_SPIKE:
                            CreateHazard(gimmicks, center, a);
                            break;

                        case GameConstants.SYM_BOUNCE_VERTICAL:
                            CreateBouncePad(gimmicks, center, a.bouncePadActive, a.bouncePadInactive,
                                new Vector2(0f, GameConstants.BOUNCE_PAD_VELOCITY),
                                new Vector2(0.875f, 0.75f), new Vector2(0f, -0.125f), "BouncePad_B");
                            break;

                        case GameConstants.SYM_BOUNCE_DIAGONAL:
                            CreateBouncePad(gimmicks, center, a.sideBouncePadActive, a.sideBouncePadInactive,
                                GameConstants.SIDE_BOUNCE_VEC,
                                GameConstants.SIDE_BOUNCE_TRIGGER_SIZE, Vector2.zero, "BouncePad_R");
                            break;

                        case GameConstants.SYM_GEAR_START:
                            CreateGearPlatform(gimmicks, map, c, r, a);
                            break;

                        case GameConstants.SYM_GEAR_RAIL:
                            CreateRail(gimmicks, center, a);
                            break;

                        case GameConstants.SYM_CORE:
                            CreateCore(gimmicks, center, a);
                            break;

                        case GameConstants.SYM_EXIT:
                            built.Exit = CreateExit(gimmicks, c, r, a);
                            break;

                        case GameConstants.SYM_PLAYER_SPAWN:
                            built.SpawnPoint = new Vector2(c + 0.5f, CellBottom(r)); // 발바닥 기준
                            break;
                    }
                }
            }

            // 크림슨 타일맵: 상태에 따라 타일 교체 + Sorting Layer 교체
            var crimsonPalette = crimson.gameObject.AddComponent<PaletteObject>();
            crimsonPalette.ConfigureTilemap(crimson, crimsonRenderer, a.tileCrimsonSolid, a.tileCrimsonSolidInactive);
            if (!crimsonUsed) crimson.gameObject.SetActive(false);

            // 콜라이더 즉시 생성
            terrainCollider.ProcessTilemapChanges();
            terrainComposite.GenerateGeometry();
            if (crimsonUsed)
            {
                crimsonCollider.ProcessTilemapChanges();
                crimsonComposite.GenerateGeometry();
            }

            // 좌/우/상단 보이지 않는 경계 (하단은 열어 두고 추락 사망 처리)
            var bounds = NewChild(root.transform, "Boundaries", Vector2.zero).transform;
            AddBoundary(bounds, "Bound_Left", new Vector2(-0.5f, H * 0.5f), new Vector2(1f, H + 4f));
            AddBoundary(bounds, "Bound_Right", new Vector2(W + 0.5f, H * 0.5f), new Vector2(1f, H + 4f));
            AddBoundary(bounds, "Bound_Top", new Vector2(W * 0.5f, H + 0.5f), new Vector2(W + 2f, 1f));

            return built;
        }

        // 맵 밖은 돌로 간주 (화면 가장자리에 외곽선을 그리지 않기 위해)
        static bool IsStone(string[] map, int c, int r)
        {
            if (c < 0 || c >= W || r < 0 || r >= H) return true;
            return map[r][c] == GameConstants.SYM_STONE;
        }

        static GameObject NewChild(Transform parent, string name, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            return go;
        }

        static Tilemap CreateTilemap(Transform grid, string name, int layer, string sortingLayer, int order, bool withCollider,
            out TilemapRenderer renderer, out TilemapCollider2D tilemapCollider, out CompositeCollider2D composite)
        {
            var go = NewChild(grid, name, Vector2.zero);
            go.layer = layer;
            var tilemap = go.AddComponent<Tilemap>();
            renderer = go.AddComponent<TilemapRenderer>();
            renderer.sortingLayerName = sortingLayer;
            renderer.sortingOrder = order;
            tilemapCollider = null;
            composite = null;
            if (withCollider)
            {
                var body = go.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Static;
                tilemapCollider = go.AddComponent<TilemapCollider2D>();
                tilemapCollider.compositeOperation = Collider2D.CompositeOperation.Merge;
                composite = go.AddComponent<CompositeCollider2D>();
                composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
                composite.generationType = CompositeCollider2D.GenerationType.Synchronous;
            }
            return tilemap;
        }

        static void CreateHazard(Transform parent, Vector2 center, PaletteShaperAssets a)
        {
            var go = NewChild(parent, "Hazard_X", center);
            go.layer = GameConstants.LAYER_CRIMSON_HAZARD;
            var sr = go.AddComponent<SpriteRenderer>();
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(GameConstants.HAZARD_HITBOX_SIZE, GameConstants.HAZARD_HITBOX_SIZE); // 8x8 px (관대한 판정)
            go.AddComponent<CrimsonHazard>().ConfigureSprite(sr, a.spikeActive, a.spikeInactive);
        }

        static void CreateBouncePad(Transform parent, Vector2 center, Sprite active, Sprite inactive, Vector2 velocity,
            Vector2 triggerSize, Vector2 triggerOffset, string name)
        {
            var go = NewChild(parent, name, center);
            go.layer = GameConstants.LAYER_CRIMSON_SOLID;
            var sr = go.AddComponent<SpriteRenderer>();
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = triggerSize;
            col.offset = triggerOffset;
            go.AddComponent<CrimsonBouncePad>().ConfigurePad(sr, active, inactive, velocity);
        }

        static void CreateGearPlatform(Transform parent, string[] map, int c, int r, PaletteShaperAssets a)
        {
            CreateRail(parent, CellCenter(c, r), a);
            int end = c;
            while (end + 1 < W && map[r][end + 1] == GameConstants.SYM_GEAR_RAIL) end++;
            float y = CellBottom(r) + 0.5f;
            var start = new Vector2(c + 1f, y);                  // 2칸 폭: G칸 + 오른쪽 1칸
            var finish = new Vector2(Mathf.Max(c + 1f, end), y); // 오른쪽 끝이 마지막 '=' 칸 오른쪽 모서리

            var go = NewChild(parent, "GearPlatform_G", start);
            go.layer = GameConstants.LAYER_CRIMSON_SOLID;
            var body = go.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            var sr = go.AddComponent<SpriteRenderer>();
            var col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(2f, 1f);
            go.AddComponent<CrimsonGearPlatform>().ConfigurePlatform(sr, a.gearPlatformActive, a.gearPlatformInactive, col, start, finish);
        }

        static void CreateRail(Transform parent, Vector2 center, PaletteShaperAssets a)
        {
            var go = NewChild(parent, "Rail", center);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = a.railTrack;
            sr.sortingLayerName = GameConstants.SORT_BG_PROPS;
        }

        static void CreateCore(Transform parent, Vector2 center, PaletteShaperAssets a)
        {
            var go = NewChild(parent, "CrimsonCore", center);
            var sr = go.AddComponent<SpriteRenderer>();
            var col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = 0.4f;
            go.AddComponent<CrimsonCore>().Configure(sr, a.crimsonCore);
        }

        /// <summary>
        /// 석문 16x32 (1x2 타일): E 칸 바닥에 붙여 위로 2칸. E가 천장 바로 아래(row 1)인 Room에서는
        /// 윗칸이 천장 타일과 겹치므로 지형 앞(Terrain_Base, order 5)에 그려 벽에 난 문처럼 보이게 한다.
        /// </summary>
        static RoomExit CreateExit(Transform parent, int c, int r, PaletteShaperAssets a)
        {
            float bottom = CellBottom(r);
            var door = NewChild(parent, "ExitDoor_E", new Vector2(c + 0.5f, bottom + 1f));
            var sr = door.AddComponent<SpriteRenderer>();
            sr.sprite = a.exitDoor;
            sr.sortingLayerName = GameConstants.SORT_TERRAIN_BASE;
            sr.sortingOrder = 5;

            var trigger = NewChild(door.transform, "ExitTrigger", new Vector2(c + 0.5f, bottom + 0.5f));
            var col = trigger.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(1f, 1f);
            return trigger.AddComponent<RoomExit>();
        }

        static void AddBoundary(Transform parent, string name, Vector2 center, Vector2 size)
        {
            var go = NewChild(parent, name, center);
            go.layer = GameConstants.LAYER_TERRAIN_BASE;
            var col = go.AddComponent<BoxCollider2D>();
            col.size = size;
        }
    }
}
