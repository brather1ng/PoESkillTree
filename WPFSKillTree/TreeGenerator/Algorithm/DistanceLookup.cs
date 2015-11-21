﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("UnitTests")]
namespace POESKillTree.TreeGenerator.Algorithm
{
    public interface IDistanceLookup
    {
        int CacheSize { get; }

        uint this[int a, int b] { get; }
    }

    public interface IDistancePathLookup : IDistanceLookup
    {
        IReadOnlyCollection<ushort> GetShortestPath(int a, int b);

        GraphNode IndexToNode(int index);
    }

    /// <summary>
    ///  Calculates and caches distances between nodes. Only relies on adjacency
    ///  information stored in the nodes.
    /// </summary>
    public class DistanceLookup : IDistancePathLookup
    {
        // The uint compounds both ushort indices.
        private Dictionary<uint, uint> _distances = new Dictionary<uint, uint>();

        private Dictionary<uint, ushort[]> _paths = new Dictionary<uint, ushort[]>();
        
        private uint[,] _distancesFast;

        private ushort[,][] _pathsFast;

        /// <summary>
        /// The GraphNodes of which distances and paths are cached.
        /// The index in the Array equals their <see cref="GraphNode.DistancesIndex"/>.
        /// </summary>
        private GraphNode[] _nodes;

        /// <summary>
        /// Whether CalculateFully got called.
        /// </summary>
        private bool _fullyCached;

        /// <summary>
        /// Number of cached nodes.
        /// </summary>
        private int _cacheSize;

        /// <summary>
        /// Gets the number of cached nodes.
        /// </summary>
        public int CacheSize
        {
            get
            {
                if (!_fullyCached)
                    throw new InvalidOperationException("CacheSize is only accessible once CalculateFully() got called!");
                return _cacheSize;
            }
        }

        /// <summary>
        ///  Retrieves the path distance from one node to another, or calculates
        ///  it if it has not yet been found and CalculateFully has not been called.
        /// </summary>
        /// <param name="a">The first graph node.</param>
        /// <param name="b">The second graph node.</param>
        /// <returns>The length of the path from a to b (equals the amount of edges
        /// traversed).</returns>
        /// <remarks>
        ///  If CalculateFully has been called and the nodes are not connected, 0 will be returned.
        ///  If CalculateFully has been called and the nodes were not both passed to it, a IndexOutOfRangeException will be thrown.
        ///  If CalculateFully has not been called and the nodes are not connected, a GraphNotConnectedException will be thrown.
        /// </remarks>
        public uint this[GraphNode a, GraphNode b]
        {
            get
            {
                if (_fullyCached)
                {
                    return _distancesFast[a.DistancesIndex, b.DistancesIndex];
                }

                var index = GetIndex(a, b);
                if (!_distances.ContainsKey(index))
                {
                    Dijkstra(a, b);
                }
                return _distances[index];
            }
        }

        /// <summary>
        /// Retrieves the path distance from one node to another.
        /// CalculateFully must have been called or an exception will be thrown.
        /// </summary>
        /// <returns>The length of the path from a to b (equals the amount of edges
        /// traversed).</returns>
        public uint this[int a, int b]
        {
            get { return _distancesFast[a, b]; }
        }

        /// <summary>
        ///  Retrieves the shortest path from one node to another, or calculates
        ///  it if it has not yet been found and CalculateFully has not been called.
        /// </summary>
        /// <param name="a">The first graph node. (not null)</param>
        /// <param name="b">The second graph node. (not null)</param>
        /// <returns>The shortest path from a to b, not containing either and ordered from a to b or b to a.</returns>
        /// <remarks>
        ///  If CalculateFully has been called and the nodes are not connected, null will be returned.
        ///  If CalculateFully has been called and the nodes were not both passed to it, a IndexOutOfRangeException will be thrown.
        ///  If CalculateFully has not been called and the nodes are not connected, a GraphNotConnectedException will be thrown.
        /// </remarks>
        private IReadOnlyCollection<ushort> GetShortestPath(GraphNode a, GraphNode b)
        {
            if (_fullyCached)
            {
                return _pathsFast[a.DistancesIndex, b.DistancesIndex];
            }

            var index = GetIndex(a, b);
            if (!_distances.ContainsKey(index))
            {
                Dijkstra(a, b);
            }
            return _paths[index];
        }

        public IReadOnlyCollection<ushort> GetShortestPath(int a, int b)
        {
            return _pathsFast[a, b];
        }

        /// <summary>
        /// Returns the GraphNode with the specified <see cref="GraphNode.DistancesIndex"/>.
        /// </summary>
        public GraphNode IndexToNode(int index)
        {
            return _nodes[index];
        }

        /// <summary>
        /// Returns whether the given nodes are connected.
        /// </summary>
        public bool AreConnected(GraphNode a, GraphNode b)
        {
            try
            {
                // Null if not connected and _fullyCached
                // Exception if not connected and not _fullyCached
                return GetShortestPath(a, b) != null;
            }
            catch (GraphNotConnectedException)
            {
                return false;
            }
        }

        /// <summary>
        ///  Compounds two ushort node indices into a single uint one, which
        ///  is independent of the order of the two indices.
        /// </summary>
        /// <param name="a">The first index.</param>
        /// <param name="b">The second index.</param>
        /// <returns>The compounded index.</returns>
        private static uint GetIndex(GraphNode a, GraphNode b)
        {
            var aId = a.Id;
            var bId = b.Id;
            return (uint)(Math.Min(aId, bId) << 16) + Math.Max(aId, bId);
        }

        private void SetFastDistance(int a, int b, uint value)
        {
            _distancesFast[a, b] = _distancesFast[b, a] = value;
        }

        private void SetFastShortestPath(int a, int b, ushort[] path)
        {
            _pathsFast[a, b] = _pathsFast[b, a] = path;
        }

        public void MergeInto(int x, int into)
        {
            if (!_fullyCached)
                throw new InvalidOperationException("Distances must be fully cached to merge nodes");

            var path = new HashSet<ushort>(GetShortestPath(x, into));
            SetFastDistance(x, into, 0);
            SetFastShortestPath(x, into, new ushort[0]);
            for (var i = 0; i < _cacheSize; i++)
            {
                if (i == into || i == x) continue;

                var ixPath = GetShortestPath(i, x).Where(n => !path.Contains(n)).ToArray();
                var iIntoPath = GetShortestPath(i, into).Where(n => !path.Contains(n)).ToArray();
                if (ixPath.Length < iIntoPath.Length)
                {
                    SetFastDistance(i, into, (uint) ixPath.Length + 1);
                    SetFastShortestPath(i, into, ixPath);
                }
                else
                {
                    SetFastDistance(i, into, (uint) iIntoPath.Length + 1);
                    SetFastShortestPath(i, into, iIntoPath);
                }
            }
        }

        /// <summary>
        /// Calculates and caches all distances between the given nodes.
        /// Enables fast lookups.
        /// Sets DistancesIndex of the nodes as incremental index in the cache starting from 0.
        /// </summary>
        /// <remarks>Calls to GetDistance and GetShortestPath after this method
        /// has been called must already be cached.</remarks>
        public void CalculateFully(List<GraphNode> nodes)
        {
            if (nodes == null) throw new ArgumentNullException("nodes");

            _cacheSize = nodes.Count;
            _nodes = new GraphNode[_cacheSize];
            for (var i = 0; i < _cacheSize; i++)
            {
                nodes[i].DistancesIndex = i;
                _nodes[i] = nodes[i];
            }
            _distancesFast = new uint[_cacheSize, _cacheSize];
            _pathsFast = new ushort[_cacheSize, _cacheSize][];

            _fullyCached = true;
            foreach (var node in nodes)
            {
                Dijkstra(node);
            }

            // No longer needed.
            _distances = null;
            _paths = null;
        }

        /// <summary>
        /// Removes the given nodes from the cache.
        /// Resets DistancesIndex of removedNodes to -1 and of remainingNodes to be
        /// incremental without holes again.
        /// O(|removedNodes| + |remainingNodes|^2)
        /// </summary>
        public List<GraphNode> RemoveNodes(IEnumerable<GraphNode> removedNodes)
        {
            if (removedNodes == null) throw new ArgumentNullException("removedNodes");

            var removed = new bool[CacheSize];
            foreach (var node in removedNodes)
            {
                removed[node.DistancesIndex] = true;
                node.DistancesIndex = -1;
            }
            var remainingNodes = new List<GraphNode>();
            for (var i = 0; i < CacheSize; i++)
            {
                if (!removed[i])
                    remainingNodes.Add(IndexToNode(i));
            }

            var oldDistances = _distancesFast;
            var oldPaths = _pathsFast;
            _cacheSize = remainingNodes.Count;
            _distancesFast = new uint[_cacheSize, _cacheSize];
            _pathsFast = new ushort[_cacheSize, _cacheSize][];

            for (var i = 0; i < _cacheSize; i++)
            {
                var oldi = remainingNodes[i].DistancesIndex;
                for (var j = 0; j < _cacheSize; j++)
                {
                    var oldj = remainingNodes[j].DistancesIndex;
                    _distancesFast[i, j] = oldDistances[oldi, oldj];
                    _pathsFast[i, j] = oldPaths[oldi, oldj];
                }
            }

            _nodes = new GraphNode[_cacheSize];
            for (var i = 0; i < _cacheSize; i++)
            {
                remainingNodes[i].DistancesIndex = i;
                _nodes[i] = remainingNodes[i];
            }

            return remainingNodes;
        }

        /// <summary>
        ///  Uses a djikstra-like algorithm to flood the graph from the start
        ///  node until the target node is found (if specified) or until all marked nodes got checked.
        /// </summary>
        /// <param name="start">The starting node. (not null)</param>
        /// <param name="target">The (optional) target node.</param>
        /// <exception cref="GraphNotConnectedException">
        /// If target node is not null and it could not be found.
        /// </exception>
        private void Dijkstra(GraphNode start, GraphNode target = null)
        {
            if (start == null) throw new ArgumentNullException("start");

            AddEdge(start, start, -1, null);
            if (start == target) return;

            // The last newly found nodes.
            var front = new HashSet<GraphNode>() { start };
            // The already visited nodes.
            var visited = new HashSet<GraphNode>() { start };
            // The dictionary of the predecessors of the visited nodes.
            var predecessors = new Dictionary<ushort, ushort>();
            // The traversed distance from the starting node in edges.
            var distFromStart = 0;

            while (front.Count > 0)
            {
                var newFront = new HashSet<GraphNode>();

                foreach (var node in front)
                {
                    foreach (var adjacentNode in node.Adjacent)
                    {
                        if (visited.Contains(adjacentNode))
                            continue;

                        predecessors[adjacentNode.Id] = node.Id;

                        if (adjacentNode == target)
                        {
                            AddEdge(start, adjacentNode, distFromStart, predecessors);
                            return;
                        }
                        if (adjacentNode.DistancesIndex >= 0)
                        {
                            AddEdge(start, adjacentNode, distFromStart, predecessors);
                        }

                        newFront.Add(adjacentNode);
                        visited.Add(adjacentNode);
                    }
                }

                front = newFront;
                distFromStart++;
            }

            // Target node was not found because start and target are not connected.
            if (target != null)
                throw new GraphNotConnectedException();
        }

        /// <summary>
        /// Adds the distance and shortest path between from and to to the respectives
        /// dictionarys if not already present.
        /// </summary>
        private void AddEdge(GraphNode from, GraphNode to, int distFromStart, IDictionary<ushort, ushort> predecessors)
        {
            var length = distFromStart + 1;

            if (_fullyCached)
            {
                var i1 = from.DistancesIndex;
                var i2 = to.DistancesIndex;
                if (_pathsFast[i1, i2] != null) return;

                var path = length > 0 ? GenerateShortestPath(from.Id, to.Id, predecessors, length) : new ushort[0];
                SetFastDistance(i1, i2, (uint)length);
                SetFastShortestPath(i1, i2, path);
            }
            else
            {
                var index = GetIndex(from, to);
                if (_distances.ContainsKey(index)) return;

                var path = length > 0 ? GenerateShortestPath(from.Id, to.Id, predecessors, length) : new ushort[0];
                _paths[index] = path;
                _distances[index] = (uint)length;
            }
        }
        
        /// <summary>
        /// Generates the shortest path from target to start by reading it out of the predecessors-dictionary.
        /// The dictionary must have a path from target to start stored.
        /// </summary>
        /// <param name="start">The starting node</param>
        /// <param name="target">The target node</param>
        /// <param name="predecessors">Dictonary with the predecessor of every node</param>
        /// <param name="length">Length of the shortest path</param>
        /// <returns>The shortest path from start to target, not including either. The Array is ordered from target to start</returns>
        private static ushort[] GenerateShortestPath(ushort start, ushort target, IDictionary<ushort, ushort> predecessors, int length)
        {
            var path = new ushort[length - 1];
            var i = 0;
            for (var node = predecessors[target]; node != start; node = predecessors[node], i++)
            {
                path[i] = node;
            }
            return path;
        }
    }
    
    public class GraphNotConnectedException : Exception
    {
    }
}