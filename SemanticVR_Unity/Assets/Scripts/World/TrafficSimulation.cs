using System.Collections.Generic;
using SemanticVR.Player;
using UnityEngine;

namespace SemanticVR.World
{
    /// <summary>
    /// Lightweight traffic and pedestrian simulation for the intersection. No physics and no per-agent MonoBehaviours:
    /// one Update steps every agent along fixed paths.
    /// Vehicles: four inbound lanes (straight through), car-following with comfortable acceleration/braking, they stop
    /// at the stop line on red (and on yellow when they can stop), keep a gap to the vehicle ahead and to the player.
    /// Pedestrians: random walk on a sidewalk/crosswalk graph; they cross only while the vehicles on the crossed road
    /// are red and the walk window is long enough, then walk with a swinging-limb animation.
    /// Agents never teleport: vehicles fade in/out at the far road ends, pedestrians turn around there.
    /// </summary>
    public sealed class TrafficSimulation : MonoBehaviour
    {
        [Header("Scene links")]
        public TrafficLightController lights;
        public Transform[] vehicles = new Transform[0];
        public Transform[] pedestrians = new Transform[0];
        public Transform player;

        [Header("Geometry (matches the Phase 1 generator)")]
        public float armLength = 75f, halfRoad = 5f, laneOffset = 2.5f, roadHeight = 0.04f, kerbHeight = 0.15f;
        public float stopLineAlong = 9.8f;

        [Header("Vehicles")]
        public float maxSpeed = 8f, accel = 2.5f, comfortBrake = 3.5f, minGap = 2.0f;
        [Header("Pedestrians")]
        public float walkSpeedMin = 1.1f, walkSpeedMax = 1.5f, sidewalkAlong = 45f;
        public int seed = 1234;

        // ---- diagnostics (read by tests / debug UI)
        public int VehicleCount => veh.Count;
        public int PedestrianCount => ped.Count;
        public int VehicleRespawns { get; private set; }
        public float MaxVehicleSpeedObserved { get; private set; }
        public float MaxPedSpeedObserved { get; private set; }
        public float MaxVehicleStepPerSecond { get; private set; }
        public int PedestriansCrossing { get; private set; }
        public int VehiclesStopped { get; private set; }
        public int RedLightViolations { get; private set; }
        /// <summary>Frames in which a pedestrian was on a crosswalk while vehicles on the crossed road did not have red.</summary>
        public int PedestrianConflictFrames { get; private set; }
        public float SimMs { get; private set; }

        // -------------------------------------------------------------------- vehicles
        sealed class Lane { public Vector3 origin, dir; public float length, stopS; public RoadAxis axis; public readonly List<Veh> list = new List<Veh>(); }
        sealed class Veh { public Transform t; public Lane lane; public float s, v, len, rampLen = 6f; public Vector3 baseScale; public bool waitingToSpawn; }
        readonly List<Lane> lanes = new List<Lane>();
        readonly List<Veh> veh = new List<Veh>();

        // ----------------------------------------------------------------- pedestrians
        sealed class Node { public Vector3 pos; public readonly List<Edge> edges = new List<Edge>(); public bool deadEnd; }
        sealed class Edge { public Node to; public bool crosswalk; public RoadAxis crossed; public float length; public Vector3 dir; }
        sealed class Ped
        {
            public Transform t; public Node from; public Edge edge; public float along, speed, wait, y, phase, swing; public Vector3 off;
            public Transform legL, legR, armL, armR, torso;
            public bool crossing; public Node prevNode;
        }
        readonly List<Node> nodes = new List<Node>();
        readonly List<Ped> ped = new List<Ped>();
        System.Random rng;
        const float PedLateral = 0.35f;

        void Start()
        {
            rng = new System.Random(seed);
            if (player == null) { var pc = FindFirstObjectByType<PlayerController>(); if (pc != null) player = pc.transform; }
            BuildLanes(); BuildVehicles(); BuildPedGraph(); BuildPedestrians();
        }

        // ============================================================== set-up
        void BuildLanes()
        {
            // outward directions N,S,E,W; vehicles approach on the right-hand lane (driver's right = cross(up, travel)).
            foreach (var (outward, axis) in new[] { (Vector3.forward, RoadAxis.NorthSouth), (Vector3.back, RoadAxis.NorthSouth), (Vector3.right, RoadAxis.EastWest), (Vector3.left, RoadAxis.EastWest) })
            {
                Vector3 travel = -outward;
                Vector3 right = Vector3.Cross(Vector3.up, travel);
                var l = new Lane
                {
                    origin = outward * (halfRoad + armLength - 3f) + right * laneOffset + Vector3.up * roadHeight,
                    dir = travel, axis = axis,
                };
                l.length = 2f * (halfRoad + armLength - 3f);
                l.stopS = (halfRoad + armLength - 3f) - stopLineAlong;   // arc position of the stop line
                lanes.Add(l);
            }
        }

        void BuildVehicles()
        {
            int perLane = Mathf.CeilToInt(vehicles.Length / (float)lanes.Count);
            for (int i = 0; i < vehicles.Length; i++)
            {
                var t = vehicles[i]; if (t == null) continue;
                var lane = lanes[i % lanes.Count];
                var v = new Veh { t = t, lane = lane, baseScale = t.localScale };
                var box = t.GetComponent<BoxCollider>();
                v.len = box != null ? box.size.z * t.lossyScale.z : (t.name.Contains("Bus") ? 11f : 4.4f);
                // stagger inside the far half of the lane so the start is calm; spacing >= 24 m
                int slot = i / lanes.Count;
                v.s = 6f + slot * (lane.stopS - 14f) / Mathf.Max(1, perLane) + (float)rng.NextDouble() * 4f;
                v.v = maxSpeed * 0.6f;
                lane.list.Add(v); veh.Add(v);
                Place(v);
            }
        }

        void Place(Veh v)
        {
            var l = v.lane;
            v.t.SetPositionAndRotation(l.origin + l.dir * v.s, Quaternion.LookRotation(l.dir));
            float ramp = Mathf.Min(Mathf.Clamp01(v.s / v.rampLen), Mathf.Clamp01((l.length - v.s) / v.rampLen));
            v.t.localScale = v.baseScale * Mathf.Max(0.001f, ramp * ramp * (3 - 2 * ramp));
        }

        void BuildPedGraph()
        {
            Node N(float x, float z, bool dead = false) { var n = new Node { pos = new Vector3(x, kerbHeight, z), deadEnd = dead }; nodes.Add(n); return n; }
            float c = halfRoad + 1.5f, w = c + 0.5f;                                   // 6.5 / 7.0
            var nE = N(c, w); var nW = N(-c, w); var sE = N(c, -w); var sW = N(-c, -w);  // ends of the north / south crosswalks
            var eN = N(w, c); var eS = N(w, -c); var wN = N(-w, c); var wS = N(-w, -c);  // ends of the east / west crosswalks
            Link(nE, nW, true, RoadAxis.NorthSouth); Link(sE, sW, true, RoadAxis.NorthSouth);
            Link(eN, eS, true, RoadAxis.EastWest); Link(wN, wS, true, RoadAxis.EastWest);
            Link(nE, eN, false, 0); Link(nW, wN, false, 0); Link(sE, eS, false, 0); Link(sW, wS, false, 0);   // round each corner
            float a = sidewalkAlong;
            Link(nE, N(c, a, true), false, 0); Link(nW, N(-c, a, true), false, 0);
            Link(sE, N(c, -a, true), false, 0); Link(sW, N(-c, -a, true), false, 0);
            Link(eN, N(a, c, true), false, 0); Link(eS, N(a, -c, true), false, 0);
            Link(wN, N(-a, c, true), false, 0); Link(wS, N(-a, -c, true), false, 0);
        }

        static void Link(Node a, Node b, bool crosswalk, RoadAxis crossed)
        {
            Vector3 d = b.pos - a.pos; float len = d.magnitude;
            a.edges.Add(new Edge { to = b, crosswalk = crosswalk, crossed = crossed, length = len, dir = d / len });
            b.edges.Add(new Edge { to = a, crosswalk = crosswalk, crossed = crossed, length = len, dir = -d / len });
        }

        void BuildPedestrians()
        {
            for (int i = 0; i < pedestrians.Length; i++)
            {
                var t = pedestrians[i]; if (t == null) continue;
                var p = new Ped { t = t, speed = Mathf.Lerp(walkSpeedMin, walkSpeedMax, (float)rng.NextDouble()), phase = (float)rng.NextDouble() * 6.28f };
                p.legL = t.Find("LegL"); p.legR = t.Find("LegR"); p.armL = t.Find("Torso/ArmL"); p.armR = t.Find("Torso/ArmR"); p.torso = t.Find("Torso");
                // start spread over the sidewalk network: random node, random edge, random progress (never inside a crosswalk)
                Edge e; Node from; int guard = 0;
                do { from = nodes[rng.Next(nodes.Count)]; e = from.edges[rng.Next(from.edges.Count)]; } while (e.crosswalk && ++guard < 50);
                p.from = from; p.edge = e; p.along = (float)rng.NextDouble() * e.length * 0.9f; p.y = kerbHeight;
                p.off = Vector3.Cross(Vector3.up, e.dir) * PedLateral;
                ped.Add(p);
                Place(p, 0f, 0f);
            }
        }

        // ============================================================== update
        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            if (dt <= 0f) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            StepVehicles(dt);
            StepPedestrians(dt);
            SimMs = Mathf.Lerp(SimMs, (float)sw.Elapsed.TotalMilliseconds, 0.05f);
        }

        // ------------------------------------------------------------- vehicles
        void StepVehicles(float dt)
        {
            int stopped = 0; float maxStep = 0;
            foreach (var l in lanes)
            {
                l.list.Sort((a, b) => b.s.CompareTo(a.s));                 // front-most first
                var state = lights != null ? lights.StateFor(l.axis) : SignalState.Green;
                for (int i = 0; i < l.list.Count; i++)
                {
                    var v = l.list[i];
                    if (v.waitingToSpawn)
                    {
                        // re-enter at the far end only when the entry is clear (no popping into another vehicle)
                        bool clear = true;
                        foreach (var o in l.list) if (o != v && !o.waitingToSpawn && o.s - o.len * 0.5f < v.len + 8f) { clear = false; break; }
                        if (!clear) continue;
                        v.waitingToSpawn = false; v.s = 0f; v.v = maxSpeed * 0.7f;
                        Place(v); v.t.gameObject.SetActive(true);
                    }
                    float front = v.s + v.len * 0.5f;
                    float target = maxSpeed;

                    // 1) traffic light
                    float gap = l.stopS - front;
                    if (gap > -0.3f && state != SignalState.Green)
                    {
                        bool canStop = v.v * v.v / (2f * comfortBrake) <= Mathf.Max(0f, gap) + 0.2f;
                        if (state == SignalState.Red || canStop) target = Mathf.Min(target, Mathf.Sqrt(2f * comfortBrake * Mathf.Max(0f, gap - 0.4f)));
                    }
                    // 2) vehicle ahead
                    Veh lead = null;
                    for (int k = i - 1; k >= 0; k--) if (!l.list[k].waitingToSpawn) { lead = l.list[k]; break; }
                    if (lead != null)
                    {
                        float g = (lead.s - lead.len * 0.5f) - front - minGap;
                        target = Mathf.Min(target, Mathf.Sqrt(lead.v * lead.v + 2f * comfortBrake * Mathf.Max(0f, g)));
                    }
                    // 3) the player standing in the lane ahead
                    if (player != null)
                    {
                        float ps = Vector3.Dot(player.position - l.origin, l.dir);
                        Vector3 lanePoint = l.origin + l.dir * ps;
                        float lateral = Vector2.Distance(new Vector2(player.position.x, player.position.z), new Vector2(lanePoint.x, lanePoint.z));
                        if (lateral < 1.7f && player.position.y < 1f && ps > front - 0.5f)
                        {
                            float g = ps - 0.4f - front - 1.5f;
                            target = Mathf.Min(target, Mathf.Sqrt(2f * comfortBrake * Mathf.Max(0f, g)));
                        }
                    }
                    // 4) smooth speed change, then integrate
                    v.v = target > v.v ? Mathf.Min(target, v.v + accel * dt) : Mathf.Max(target, v.v - 5f * dt);
                    v.v = Mathf.Max(0f, v.v);
                    float step = v.v * dt; float frontBefore = front; v.s += step;
                    if (frontBefore <= l.stopS && v.s + v.len * 0.5f > l.stopS && state == SignalState.Red) RedLightViolations++;
                    maxStep = Mathf.Max(maxStep, step / dt);
                    MaxVehicleSpeedObserved = Mathf.Max(MaxVehicleSpeedObserved, v.v);
                    if (v.v < 0.1f) stopped++;
                    if (v.s >= l.length) { v.waitingToSpawn = true; v.t.gameObject.SetActive(false); VehicleRespawns++; continue; }
                    Place(v);
                }
            }
            VehiclesStopped = stopped; MaxVehicleStepPerSecond = maxStep;
        }

        // --------------------------------------------------------- pedestrians
        void StepPedestrians(float dt)
        {
            int crossing = 0;
            foreach (var p in ped)
            {
                if (p.wait > 0f) { p.wait -= dt; Animate(p, 0f, dt); continue; }
                var e = p.edge;
                // a pedestrian about to step onto a crosswalk waits for the walk window
                if (p.along <= 0.01f && e.crosswalk && !MayCross(e))
                {
                    p.wait = 0.25f; Animate(p, 0f, dt); continue;
                }
                // pause if the player stands right in front
                if (player != null)
                {
                    Vector3 to = player.position - p.t.position; to.y = 0;
                    if (to.sqrMagnitude < 1.1f * 1.1f && Vector3.Dot(to.normalized, e.dir) > 0.6f) { Animate(p, 0f, dt); continue; }
                }
                float step = p.speed * dt;
                p.along += step;
                MaxPedSpeedObserved = Mathf.Max(MaxPedSpeedObserved, p.speed);
                if (e.crosswalk) { crossing++; if (lights != null && lights.StateFor(e.crossed) != SignalState.Red) PedestrianConflictFrames++; }
                if (p.along >= e.length)
                {
                    var arrived = e.to; var came = p.from;
                    p.from = arrived; p.prevNode = came;
                    if (arrived.deadEnd) p.wait = 0.5f + (float)rng.NextDouble() * 2.5f;   // linger, then walk back
                    p.edge = ChooseEdge(arrived, came); p.along = 0f;
                }
                Place(p, step, dt);
                Animate(p, p.speed, dt);
            }
            PedestriansCrossing = crossing;
        }

        bool MayCross(Edge e)
        {
            if (lights == null) return true;
            float need = e.length / walkSpeedMin + 1.5f;
            return lights.StateFor(e.crossed) == SignalState.Red && lights.SecondsUntilNotRed(e.crossed) > need;
        }

        Edge ChooseEdge(Node at, Node came)
        {
            if (at.edges.Count == 1) return at.edges[0];
            float total = 0; foreach (var e in at.edges) total += Weight(e, came);
            float r = (float)rng.NextDouble() * total;
            foreach (var e in at.edges) { r -= Weight(e, came); if (r <= 0) return e; }
            return at.edges[0];
        }
        static float Weight(Edge e, Node came) => e.to == came ? 0.1f : (e.crosswalk ? 2f : 1f);

        void Place(Ped p, float step, float dt)
        {
            var e = p.edge;
            Vector3 pos = p.from.pos + e.dir * Mathf.Min(p.along, e.length);
            Vector3 right = Vector3.Cross(Vector3.up, e.dir);
            // keep to the right of the travel direction; ease the offset when the direction changes
            p.off = dt > 0 ? Vector3.MoveTowards(p.off, right * PedLateral, dt * 1.2f) : right * PedLateral;
            // kerb height: down on the crosswalk (road surface), up on the footpath
            float targetY = e.crosswalk && p.along > 0.9f && p.along < e.length - 0.9f ? roadHeight : kerbHeight;
            p.y = dt > 0 ? Mathf.MoveTowards(p.y, targetY, dt * 0.5f) : targetY;
            pos += p.off; pos.y = p.y;
            p.t.position = pos;
            if (e.dir.sqrMagnitude > 0.01f)
            {
                var want = Quaternion.LookRotation(e.dir);
                p.t.rotation = dt > 0 ? Quaternion.Slerp(p.t.rotation, want, 1f - Mathf.Exp(-10f * dt)) : want;
            }
        }

        static void Animate(Ped p, float speed, float dt)
        {
            if (p.legL == null) return;
            p.phase += speed * dt * 4.4f;                     // stride rate follows speed
            float target = speed > 0.05f ? 1f : 0f;
            p.swing = Mathf.MoveTowards(p.swing, target, dt * 4f);
            float a = Mathf.Sin(p.phase) * 32f * p.swing;
            p.legL.localRotation = Quaternion.Euler(a, 0, 0);
            p.legR.localRotation = Quaternion.Euler(-a, 0, 0);
            if (p.armL != null) p.armL.localRotation = Quaternion.Euler(-a * 0.8f, 0, 0);
            if (p.armR != null) p.armR.localRotation = Quaternion.Euler(a * 0.8f, 0, 0);
            if (p.torso != null) p.torso.localPosition = new Vector3(0, Mathf.Abs(Mathf.Cos(p.phase)) * 0.025f * p.swing, 0);
        }
    }
}
