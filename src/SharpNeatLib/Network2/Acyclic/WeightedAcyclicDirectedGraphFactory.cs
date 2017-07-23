﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using SharpNeat.Network.Analysis;

namespace SharpNeat.Network2.Acyclic
{
    public static class WeightedAcyclicDirectedGraphFactory<T>
        where T : struct
    {
        #region Public Static Methods

        public static WeightedAcyclicDirectedGraph<T> Create(IList<IWeightedDirectedConnection<T>> connectionList, int inputCount, int outputCount)
        {
            // Invoke the standard graph factory.
            WeightedDirectedGraph<T> digraph = WeightedDirectedGraphFactory<T>.Create(connectionList, inputCount, outputCount);

            // Calc the depth of each node in the digraph.
            GraphDepthInfo depthInfo = AcyclicGraphDepthAnalysis.CalculateNodeDepths(digraph);

            // Debug assert that all input nodes are at depth zero.
            // Any input node with a non-zero depth must have an input connection, and this is not supported.
            Debug.Assert(AreZero(depthInfo._nodeDepthArr, inputCount));

            // Compile a mapping from current node IDs to new IDs (based on node depth in the graph).
            int[] newIdMap = CompileNodeIdMap(depthInfo, digraph.TotalNodeCount, inputCount);

            // Map the connection node IDs.
            DirectedConnection[] connArr = digraph.ConnectionArray;
            MapIds(connArr, newIdMap);

            // Sort the connections based on sourceID, TargetId; this will arrange the connections based on the depth 
            // of the source nodes.
            // Note. This overload of Aray.Sort will also sort a second array, i.e. keep the items in both arrays aligned;
            // here we use this to keep the weights aligned with their associated DirectedConnection.
            T[] weightArr = digraph.WeightArray;
            Array.Sort(connArr, weightArr, DirectedConnectionComparer.__Instance);

            // Make a copy of the sub-range of newIdMap that represents the output nodes.
            // This is required later to be able to locate the output nodes now that they have been sorted by depth.
            int[] outputNeuronIdxArr = new int[outputCount];
            Array.Copy(newIdMap, inputCount, outputNeuronIdxArr, 0, outputCount);

            // Create an array of LayerInfo(s).
            // Each LayerInfo contains the index + 1 of both the last node and last connection in that layer.
            //
            // The array is in order of depth, from layer zero (inputs nodes) to the last layer (usually output nodes,
            // but not necessarily if there is a dead end pathway with a high number of hops).
            //
            // Note. There is guaranteed to be at least one connection with a source at a given depth level, this is
            // because for there to be a layer N there must necessarily be a connection from a node in layer N-1 
            // to a node in layer N.
            int netDepth = depthInfo._networkDepth;
            LayerInfo[] layerInfoArr = new LayerInfo[netDepth];

            // Scanning over nodes can start at inputAndBiasCount instead of zero, 
            // because we know that all nodes prior to that index are at depth zero.
            int nodeCount = digraph.TotalNodeCount;
            int nodeIdx = inputCount;
            int connIdx = 0;

            int[] nodeDepthArr = depthInfo._nodeDepthArr;

            for(int currDepth=0; currDepth < netDepth; currDepth++)
            {
                // Scan for last node at the current depth.
                for(; nodeIdx < nodeCount && nodeDepthArr[nodeIdx] == currDepth; nodeIdx++);
                
                // Scan for last connection at the current depth.
                for(; connIdx < connArr.Length && nodeDepthArr[connArr[connIdx].SourceId] == currDepth; connIdx++);

                // Store node and connection end indexes for the layer.
                layerInfoArr[currDepth] = new LayerInfo(nodeIdx, connIdx);
            }

            // Construct and return.
            return new WeightedAcyclicDirectedGraph<T>(
                connArr, inputCount, outputCount, nodeCount,
                layerInfoArr, weightArr);
        }

        #endregion

        #region Private Static Methods

        private static int[] CompileNodeIdMap(GraphDepthInfo depthInfo, int nodeCount, int inputCount)
        {
            // Create an array of all node IDs in the digraph.
            int[] nodeIdArr = new int[nodeCount];
            for(int i=0; i<nodeCount; i++) {
                nodeIdArr[i] = i;
            }

            // Sort nodeIdArr based on the depth of the nodes.
            // Note. We skip the input nodes because these all have depth zero, and we don't want a potentially 
            // unstable sort to change their order.
            Array.Sort(depthInfo._nodeDepthArr, nodeIdArr, inputCount, nodeCount - inputCount);

            // Each node is now assigned a new node ID based on its index in nodeIdArr, i.e.
            // we are re-allocating IDs based on node depth.
            int[] newIdByOldId = new int[nodeCount];
            for(int i=0; i<nodeCount; i++) {
                newIdByOldId[nodeIdArr[i]] = i;
            }

            return newIdByOldId;
        }

        private static void MapIds(
            DirectedConnection[] connArr,
            int[] nodeIdMap)
        {
            for(int i=0; i < connArr.Length; i++)
            {
                connArr[i] = new DirectedConnection(
                    nodeIdMap[connArr[i].SourceId],
                    nodeIdMap[connArr[i].TargetId]
                    );
            }
        }

        private static bool AreZero(int[] arr, int length)
        {
            for(int i=0; i<length; i++)
            {
                if(0 != arr[i]) {
                    return false;
                }
            }
            return true;
        }

        #endregion
    }
}
