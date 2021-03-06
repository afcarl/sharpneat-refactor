﻿using Redzen.Structures;
using SharpNeat.Neat.Genome;
using SharpNeat.Neat.Reproduction.Asexual.WeightMutation;

namespace SharpNeat.Neat.Reproduction.Asexual.Strategy
{
    public class MutateWeightsStrategy<T> : IAsexualReproductionStrategy<T>
        where T : struct
    {
        readonly MetaNeatGenome<T> _metaNeatGenome;
        readonly INeatGenomeBuilder<T> _genomeBuilder;
        readonly Int32Sequence _genomeIdSeq;
        readonly Int32Sequence _generationSeq;
        readonly WeightMutationScheme<T> _weightMutationScheme;

        #region Constructor

        public MutateWeightsStrategy(
            MetaNeatGenome<T> metaNeatGenome,
            INeatGenomeBuilder<T> genomeBuilder,
            Int32Sequence genomeIdSeq,
            Int32Sequence generationSeq,
            WeightMutationScheme<T> weightMutationScheme)
        {
            _metaNeatGenome = metaNeatGenome;
            _genomeBuilder = genomeBuilder;
            _genomeIdSeq = genomeIdSeq;
            _generationSeq = generationSeq;
            _weightMutationScheme = weightMutationScheme;
        }

        #endregion

        #region Public Methods

        public NeatGenome<T> CreateChildGenome(NeatGenome<T> parent)
        {
            // Clone the parent's connection weight array.
            var weightArr = (T[])parent.ConnectionGenes._weightArr.Clone();

            // Apply mutation to the connection weights.
            _weightMutationScheme.MutateWeights(weightArr);

            // Create the child genome's ConnectionGenes object.
            // Note. The parent genome's connection arrays are re-used; these remain unchanged because we are mutating 
            // connection *weights* only, so we can avoid the cost of cloning these arrays.
            var connGenes = new ConnectionGenes<T>(
                parent.ConnectionGenes._connArr,
                weightArr);

            // Create and return a new genome.
            // Note. The parent's ConnectionIndexArray and HiddenNodeIdArray can be re-used here because the new genome
            // has the same set of connections (same neural net structure).
            return _genomeBuilder.Create(
                _genomeIdSeq.Next(), 
                _generationSeq.Peek,
                connGenes,
                parent.HiddenNodeIdArray,
                parent.NodeIndexByIdMap,
                parent.DirectedGraph,
                parent.ConnectionIndexMap);
        }

        #endregion
    }
}
