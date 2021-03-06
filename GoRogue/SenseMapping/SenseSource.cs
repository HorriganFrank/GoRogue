﻿using GoRogue.MapViews;
using System;
using System.Collections.Generic;

namespace GoRogue.SenseMapping
{
    /// <summary>
    /// Different types of algorithms that model how values spread from the source.
    /// </summary>
    public enum SourceType
    {
        /// <summary>
        /// Performs calculation by pushing values out from the source location. Source values spread
        /// around corners a bit.
        /// </summary>
        RIPPLE,

        /// <summary>
        /// Similar to RIPPLE but with different spread mechanics. Values spread around edges like
        /// smoke or water, but maintains a tendency to curl towards the start position as it goes
        /// around edges.
        /// </summary>
        RIPPLE_LOOSE,

        /// <summary>
        /// Similar to RIPPLE, but values spread around corners only very slightly.
        /// </summary>
        RIPPLE_TIGHT,

        /// <summary>
        /// Similar to RIPPLE, but values spread around corners a lot.
        /// </summary>
        RIPPLE_VERY_LOOSE,

        /// <summary>
        /// Uses a Shadowcasting algorithm. All partially resistant grid locations are treated as
        /// being fully transparent (it's on-off blocking, where 1.0 in the resistance map blocks,
        /// and all lower values don't). Returns percentage from 1.0 at center of source to 0.0
        /// outside of range of source.
        /// </summary>
        SHADOW
    };

    /// <summary>
    /// Represents a source location to be used in a SenseMap. One would typically create these and
    /// call SenseMap.AddSenseSource with them, and perhaps retain a reference for the sake of moving
    /// it around or toggling it on-off. The player might have one of these that follows it around if
    /// SenseMap is being used as a lighting map, for instance. Note that changing values such as
    /// Position and Radius after the source is created is possible, however changes will not be
    /// reflected in any SenseSources using this source until their next Calculate call.
    /// </summary>
    public class SenseSource
    {
        // Local calculation arrays, internal so SenseMap can easily copy them.
        internal double[,] light;

        internal bool[,] nearLight;
        internal IMapView<double> resMap;
        private static readonly string[] typeWriteVals = Enum.GetNames(typeof(SourceType));
        private int _radius;

        private int size;

        /// <summary>
        /// Constructor. Takes all initial parameters, and allocates the necessary underlying arrays
        /// used for calculations.
        /// </summary>
        /// <param name="type">The spread mechanics to use for source values.</param>
        /// <param name="position">The position on a map that the source is located at.</param>
        /// <param name="radius">
        /// The maximum radius of the source -- this is the maximum distance the source values will
        /// emanate, provided the area is completely unobstructed.
        /// </param>
        /// <param name="distanceCalc">
        /// The distance calculation used to determine what shape the radius has (or a type
        /// implicitly convertible to Distance, eg. Radius).
        /// </param>
        public SenseSource(SourceType type, Coord position, int radius, Distance distanceCalc)
        {
            Type = type;
            Position = position;
            Radius = radius; // Arrays are initialized by this setter
            DistanceCalc = distanceCalc;

            resMap = null;
            Enabled = true;
        }

        /// <summary>
        /// Constructor. Takes all initial parameters, and allocates the necessary underlying arrays
        /// used for calculations.
        /// </summary>
        /// <param name="type">The spread mechanics to use for source values.</param>
        /// <param name="positionX">
        /// The X-value of the position on a map that the source is located at.
        /// </param>
        /// <param name="positionY">
        /// The Y-value of the position on a map that the source is located at.
        /// </param>
        /// <param name="radius">
        /// The maximum radius of the source -- this is the maximum distance the source values will
        /// emanate, provided the area is completely unobstructed.
        /// </param>
        /// <param name="distanceCalc">
        /// The distance calculation used to determine what shape the radius has (or a type
        /// implicitly convertible to Distance, eg. Radius).
        /// </param>
        public SenseSource(SourceType type, int positionX, int positionY, int radius, Distance distanceCalc)
            : this(type, Coord.Get(positionX, positionY), radius, distanceCalc) { }

        /// <summary>
        /// The distance calculation used to determine what shape the radius has (or a type
        /// implicitly convertible to Distance, eg. Radius)
        /// </summary>
        public Distance DistanceCalc { get; set; }

        /// <summary>
        /// Whether or not this source is enabled. If a source is disabled when its SenseMap's
        /// Calculate function is called, the source does not calculate values and is effectively
        /// assumed to be "off".
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// The position on a map that the source is located at.
        /// </summary>
        public Coord Position { get; set; }

        /// <summary>
        /// The maximum radius of the source -- this is the maximum distance the source values will
        /// emanate, provided the area is completely unobstructed. Changing this will trigger
        /// resizing (re-allocation) of the underlying arrays. However, data is not copied over --
        /// there is no need to since Calculate in SenseMap immediately copies values from local
        /// array to its "master" array.
        /// </summary>
        public int Radius
        {
            get => _radius;
            set
            {
                if (_radius != value)
                {
                    _radius = value;
                    size = _radius * 2 + 1;
                    light = new double[size, size];
                    nearLight = new bool[size, size];
                }
            }
        }

        /// <summary>
        /// The spread mechanics to use for source values.
        /// </summary>
        public SourceType Type { get; set; }

        /// <summary>
        /// Returns a string representation of the configuration of this SenseSource.
        /// </summary>
        /// <returns>A string representation of the configuration of this SenseSource.</returns>
        public override string ToString() => $"Enabled: {Enabled}, Type: {typeWriteVals[(int)Type]}, Radius Mode: {(Radius)DistanceCalc}, Position: {Position}, Radius: {Radius}";

        // Set from lighting, just so we have a reference.

        // 2 * Radius + 1 -- the width/height dimension of the local arrays.
        internal void calculateLight()
        {
            if (Enabled)
            {
                switch (Type)
                {
                    case SourceType.RIPPLE:
                    case SourceType.RIPPLE_LOOSE:
                    case SourceType.RIPPLE_TIGHT:
                    case SourceType.RIPPLE_VERY_LOOSE:
                        initArrays();
                        doRippleFOV(rippleValue(Type), resMap);
                        break;

                    case SourceType.SHADOW:
                        initArrays();
                        foreach (Direction d in AdjacencyRule.DIAGONALS.DirectionsOfNeighbors())
                        {
                            shadowCast(1, 1.0, 0.0, 0, d.DeltaX, d.DeltaY, 0, resMap);
                            shadowCast(1, 1.0, 0.0, d.DeltaX, 0, 0, d.DeltaY, resMap);
                        }
                        break;
                }
            }
        }

        private static int rippleValue(SourceType type)
        {
            switch (type)
            {
                case SourceType.RIPPLE:
                    return 2;

                case SourceType.RIPPLE_LOOSE:
                    return 3;

                case SourceType.RIPPLE_TIGHT:
                    return 1;

                case SourceType.RIPPLE_VERY_LOOSE:
                    return 6;

                default:
                    Console.Error.WriteLine("Unrecognized ripple type, defaulting to RIPPLE...");
                    return rippleValue(SourceType.RIPPLE);
            }
        }

        private void doRippleFOV(int ripple, IMapView<double> map)
        {
            double rad = Math.Max(1, Radius);
            double decay = 1.0 / (rad + 1);

            LinkedList<Coord> dq = new LinkedList<Coord>();
            dq.AddLast(Coord.Get(size / 2, size / 2)); // Add starting point
            while (!(dq.Count == 0))
            {
                Coord p = dq.First.Value;
                dq.RemoveFirst();

                if (light[p.X, p.Y] <= 0 || nearLight[p.X, p.Y])
                    continue; // Nothing left to spread!

                foreach (Direction dir in AdjacencyRule.EIGHT_WAY.DirectionsOfNeighbors())
                {
                    int x2 = p.X + dir.DeltaX;
                    int y2 = p.Y + dir.DeltaY;
                    int globalX2 = Position.X - Radius + x2;
                    int globalY2 = Position.Y - Radius + y2;

                    if (globalX2 < 0 || globalX2 >= map.Width || globalY2 < 0 || globalY2 >= map.Height || // Bounds check
                        DistanceCalc.Calculate(size / 2, size / 2, x2, y2) > rad) // +1 covers starting tile at least
                        continue;

                    double surroundingLight = nearRippleLight(x2, y2, globalX2, globalY2, ripple, decay, map);
                    if (light[x2, y2] < surroundingLight)
                    {
                        light[x2, y2] = surroundingLight;
                        if (map[globalX2, globalY2] < 1) // Not a wall (fully blocking)
                            dq.AddLast(Coord.Get(x2, y2)); // Need to redo neighbors, since we just changed this entry's light.
                    }
                }
            }
        }

        // Initializes arrays.
        private void initArrays() // Prep for lighting calculations
        {
            Array.Clear(light, 0, light.Length);
            // Any times 2 is even, plus one is odd. rad 3, 3*2 = 6, +1 = 7. 7/2=3, so math works
            int center = size / 2;
            light[center, center] = 1; // source light is center, starts out at 1
            Array.Clear(nearLight, 0, nearLight.Length);
        }

        // TODO: Make these virtual, to allow directional light sources?
        private double nearRippleLight(int x, int y, int globalX, int globalY, int rippleNeighbors, double decay, IMapView<double> map)
        {
            if (x == size / 2 && y == size / 2)
                return 1;

            List<Coord> neighbors = new List<Coord>();
            double tmpDistance = 0, testDistance;
            Coord c;

            foreach (Direction di in AdjacencyRule.EIGHT_WAY.DirectionsOfNeighbors())
            {
                int x2 = x + di.DeltaX;
                int y2 = y + di.DeltaY;
                int globalX2 = Position.X - Radius + x2;
                int globalY2 = Position.Y - Radius + y2;

                if (globalX2 >= 0 && globalX2 < map.Width && globalY2 >= 0 && globalY2 < map.Height)
                {
                    tmpDistance = DistanceCalc.Calculate(size / 2, size / 2, x2, y2);
                    int idx = 0;

                    for (int i = 0; i < neighbors.Count && i <= rippleNeighbors; i++)
                    {
                        c = neighbors[i];
                        testDistance = DistanceCalc.Calculate(size / 2, size / 2, c.X, c.Y);
                        if (tmpDistance < testDistance)
                            break;

                        idx++;
                    }
                    neighbors.Insert(idx, Coord.Get(x2, y2));
                }
            }
            if (neighbors.Count == 0)
                return 0;

            neighbors = neighbors.GetRange(0, Math.Min(neighbors.Count, rippleNeighbors));

            double curLight = 0;
            int lit = 0, indirects = 0;
            foreach (Coord p in neighbors)
            {
                int gpx = Position.X - Radius + p.X;
                int gpy = Position.Y - Radius + p.Y;
                if (light[p.X, p.Y] > 0)
                {
                    lit++;
                    if (nearLight[p.X, p.Y])
                        indirects++;

                    double dist = DistanceCalc.Calculate(x, y, p.X, p.Y);
                    double resistance = map[gpx, gpy];
                    if (gpx == Position.X && gpy == Position.Y)
                        resistance = 0.0;

                    curLight = Math.Max(curLight, light[p.X, p.Y] - dist * decay - resistance);
                }
            }

            if (map[globalX, globalY] >= 1 || indirects >= lit)
                nearLight[x, y] = true;

            return curLight;
        }

        private void shadowCast(int row, double start, double end, int xx, int xy, int yx, int yy, IMapView<double> map)
        {
            int radius = Math.Max(1, Radius);
            double decay = 1.0 / (radius + 1);

            double newStart = 0;
            if (start < end)
                return;

            bool blocked = false;
            for (int distance = row; distance <= radius && distance < size + size && !blocked; distance++)
            {
                int deltaY = -distance;
                for (int deltaX = -distance; deltaX <= 0; deltaX++)
                {
                    int currentX = size / 2 + deltaX * xx + deltaY * xy;
                    int currentY = size / 2 + deltaX * yx + deltaY * yy;
                    int gCurrentX = Position.X - Radius + currentX;
                    int gCurrentY = Position.Y - Radius + currentY;
                    double leftSlope = (deltaX - 0.5f) / (deltaY + 0.5f);
                    double rightSlope = (deltaX + 0.5f) / (deltaY - 0.5f);

                    if (!(gCurrentX >= 0 && gCurrentY >= 0 && gCurrentX < map.Width && gCurrentY < map.Height) || start < rightSlope)
                        continue;

                    if (end > leftSlope)
                        break;

                    double deltaRadius = DistanceCalc.Calculate(deltaX, deltaY);
                    if (deltaRadius <= radius)
                    {
                        double bright = 1 - decay * deltaRadius;
                        light[currentX, currentY] = bright;
                    }

                    if (blocked) // Previous cell was blocked
                    {
                        if (map[gCurrentX, gCurrentY] >= 1) // Hit a wall...
                            newStart = rightSlope;
                        else
                        {
                            blocked = false;
                            start = newStart;
                        }
                    }
                    else
                    {
                        if (map[gCurrentX, gCurrentY] >= 1 && distance < radius) // Wall within FOV
                        {
                            blocked = true;
                            shadowCast(distance + 1, start, leftSlope, xx, xy, yx, yy, map);
                            newStart = rightSlope;
                        }
                    }
                }
            }
        }
    }
}