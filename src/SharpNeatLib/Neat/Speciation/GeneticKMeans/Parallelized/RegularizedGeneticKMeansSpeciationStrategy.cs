﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Redzen.Random;
using SharpNeat.Neat.DistanceMetrics;
using SharpNeat.Neat.Genome;
using SharpNeat.Neat.Speciation.Parallelized;

namespace SharpNeat.Neat.Speciation.GeneticKMeans.Parallelized
{
    /// <summary>
    /// A speciation strategy that assigns genomes to species using k-means clustering on the genes of each genome.
    /// </summary>
    /// 
    /// <remarks>
    /// This class applies a regularized k-means method as described in this paper:
    ///    "REGULARISED k-MEANS CLUSTERING FOR DIMENSION REDUCTION APPLIED TO SUPERVISED CLASSIFICATION", 
    ///    Vladimir Nikulin, Geoffrey J. McLachlan, Department of Mathematics, University of Queensland, Brisbane, Australia.
    ///    https://people.smp.uq.edu.au/GeoffMcLachlan/cibb/nm_cibb09.pdf
    /// 
    /// The intent of regularization is to discourage formation of large dominating clusters (species), and instead to 
    /// encourage more even distribution of genomes amongst clusters, and also the formation of more stable clusters.
    /// 
    /// Regularization works as follows. In standard k-means the genomes are allocated to species who's centroid they are
    /// nearest to, the regularization method in use here adjusts the calculated genome-centroid distances to include an 
    /// additional regularization term, like so:
    /// 
    ///     adjustedDistance = distance + regularitationTerm
    ///     
    /// The regularization term (r) is calculated as follows:
    /// 
    ///     r = (c/populationSize) * L * alpha
    ///     
    /// Where:
    ///     c is a cluster size (species size).
    ///     alpha is a constant scaling factor.
    ///     L is the maximum distance between any two species centroid.
    ///     
    /// Thus, the term (c/populationSize) is a proportion ranging over the interval [0,1], where small clusters are
    /// near to zero and large cluster are nearer 1. As such the regularization term will be higher for larger clusters
    /// and therefore any genomes on he edges of a large cluster may be allocated to a nearby smaller cluster instead.
    /// 
    /// L is intended to represent the magnitudes of the distances being dealt with, i.e. it makes the regularization 
    /// method as a whole 'scale free'. The calculation for L used in this class differs from that used in the paper
    /// referred to above, but the intention is the same, i.e. to obtain some stable value that is representative of
    /// the magnitude of the distances being dealt with.
    /// 
    /// In the referred to paper L is taken to be the maximum distance between any genome and any specie centroid. In 
    /// this class we take the maximum distance between any two species centroids, this should result in a scale free
    /// distance that serves the same purpose, but that is faster to compute. This version of L will tend to be smaller
    /// that the version used in the paper, but we can adjust alpha (the constant scaling factor) accordingly.
    /// 
    /// At time of writing this class is experimental and has not been scientifically examined for suitability or 
    /// efficacy in particular in comparison to the standard k-means method.
    /// </remarks>
    /// <typeparam name="T">Connection weight and input/output numeric type (double or float).</typeparam>
    public class RegularizedGeneticKMeansSpeciationStrategy<T> : ISpeciationStrategy<NeatGenome<T>, T>
        where T : struct
    {
        #region Instance Fields

        readonly IDistanceMetric<T> _distanceMetric;
        readonly int _maxKMeansIters;
        readonly double _regularizationConstant;
        readonly ParallelOptions _parallelOptions;
        readonly GeneticKMeansSpeciationInit<T> _kmeansInit;

        #endregion
        
        #region Constructors
        
        public RegularizedGeneticKMeansSpeciationStrategy(
            IDistanceMetric<T> distanceMetric,
            int maxKMeansIters,
            double regularizationConstant,
            IRandomSource rng)
        {
            _distanceMetric = distanceMetric;
            _maxKMeansIters = maxKMeansIters;
            _regularizationConstant = regularizationConstant;
            _parallelOptions = new ParallelOptions();
            _kmeansInit = new GeneticKMeansSpeciationInit<T>(distanceMetric, _parallelOptions, rng);
        }

        public RegularizedGeneticKMeansSpeciationStrategy(
            IDistanceMetric<T> distanceMetric,
            int maxKMeansIters,
            double regularizationConstant,
            IRandomSource rng,
            ParallelOptions parallelOptions)
        {
            _distanceMetric = distanceMetric;
            _maxKMeansIters = maxKMeansIters;
            _regularizationConstant = regularizationConstant;
            _parallelOptions = parallelOptions;
            _kmeansInit = new GeneticKMeansSpeciationInit<T>(distanceMetric, _parallelOptions, rng);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Initialise a new set of species based on the provided population of genomes and the 
        /// speciation method in use.
        /// </summary>
        /// <param name="genomeList">The genomes to speciate.</param>
        /// <param name="speciesCount">The number of required species.</param>
        /// <returns>A new array of species.</returns>
        public Species<T>[] SpeciateAll(IList<NeatGenome<T>> genomeList, int speciesCount)
        {
            if(genomeList.Count < speciesCount) {
                throw new ArgumentException("The number of genomes is less than speciesCount.");
            }

            // Initialise using k-means++ initialisation method.
            var speciesArr = _kmeansInit.InitialiseSpecies(genomeList, speciesCount);

            // Run the k-means algorithm.
            RunKMeans(speciesArr);

            // Return the initialised species array.
            return speciesArr;
        }

        /// <summary>
        /// Merge new genomes into an existing set of species.
        /// </summary>
        /// <param name="genomeList">A list of genomes that have not yet been assigned a species.</param>
        /// <param name="speciesArr">An array of pre-existing species</param>
        public void SpeciateAdd(IList<NeatGenome<T>> genomeList, Species<T>[] speciesArr)
        {
            GetPopulationCountAndMaxIntraSpeciesDistance(speciesArr, out double populationCount, out double maxIntraSpeciesDistance);

            // Create a temporary working array of species modification bits.
            var updateBits = new bool[speciesArr.Length];

            // Allocate the new genomes to the species centroid they are nearest too.
            Parallel.ForEach(genomeList, _parallelOptions, (genome) =>
            {
                var nearestSpeciesIdx = DetermineGenomeSpecies(genome, speciesArr, populationCount, maxIntraSpeciesDistance);
                var nearestSpecies = speciesArr[nearestSpeciesIdx];

                lock(nearestSpecies.GenomeList) {
                    nearestSpecies.GenomeList.Add(genome);
                }

                // Set the modification bit for the species.
                updateBits[nearestSpeciesIdx] = true;
            });

            // Recalc the species centroids for species that have been modified.
            RecalcCentroids_GenomeList(speciesArr, updateBits);

            // Run the k-means algorithm.
            RunKMeans(speciesArr, populationCount, maxIntraSpeciesDistance);
        }

        #endregion

        #region Private Methods [KMeans Algorithm]

        private void RunKMeans(Species<T>[] speciesArr)
        {
            // Initialise.
            KMeansInit(speciesArr, out double populationCount, out double maxIntraSpeciesDistance);

            // Create a temporary working array of species modification bits.
            var updateBits = new bool[speciesArr.Length];

            // The k-means iterations.
            for(int iter=0; iter < _maxKMeansIters; iter++)
            {
                int reallocCount = KMeansIteration(speciesArr, updateBits, populationCount, maxIntraSpeciesDistance);
                if(0 == reallocCount) 
                {   
                    // The last k-means iteration made no re-allocations, therefore the k-means clusters are stable.
                    break;
                }
            }

            // Complete.
            KMeansComplete(speciesArr);
        }

        private void RunKMeans(Species<T>[] speciesArr, double populationCount, double maxIntraSpeciesDistance)
        {
            // Initialise.
            KMeansInit(speciesArr);

            // Create a temporary working array of species modification bits.
            var updateBits = new bool[speciesArr.Length];

            // The k-means iterations.
            for(int iter=0; iter < _maxKMeansIters; iter++)
            {
                int reallocCount = KMeansIteration(speciesArr, updateBits, populationCount, maxIntraSpeciesDistance);
                if(0 == reallocCount) 
                {   
                    // The last k-means iteration made no re-allocations, therefore the k-means clusters are stable.
                    break;
                }
            }

            // Complete.
            KMeansComplete(speciesArr);
        }

        private int KMeansIteration(Species<T>[] speciesArr, bool[] updateBits, double populationCount, double maxIntraSpeciesDistance)
        {
            int reallocCount = 0;
            Array.Clear(updateBits, 0, updateBits.Length);

            // Loop species.
            // Note. The nested parallel loop here is intentional and should give good thread concurrency in the general case.
            // For more info see: "Is it ok to use nested Parallel.For loops?" 
            // https://blogs.msdn.microsoft.com/pfxteam/2012/03/14/is-it-ok-to-use-nested-parallel-for-loops/
            //
            Parallel.For(0, speciesArr.Length, _parallelOptions, (speciesIdx) => 
            {
                var species = speciesArr[speciesIdx];

                // Loop genomes in the current species.
                Parallel.ForEach(species.GenomeById.Values, _parallelOptions, (genome) =>
                {
                    // Determine the species centroid the genome is nearest to.
                    var nearestSpeciesIdx = DetermineGenomeSpecies(genome, speciesArr, populationCount, maxIntraSpeciesDistance);

                    // If the nearest species is not the species the genome is currently in then move the genome.
                    if(nearestSpeciesIdx != speciesIdx)
                    {
                        // Move genome.
                        // Note. We can't modify species.GenomeById while we are enumerating through it, therefore we record the IDs
                        // of the genomes to be removed and remove them once we leave the enumeration loop.
                        lock(species.PendingRemovesList) {
                            species.PendingRemovesList.Add(genome.Id);
                        }

                        var nearestSpecies = speciesArr[nearestSpeciesIdx];
                        lock(nearestSpecies.PendingAddsList) {
                            nearestSpecies.PendingAddsList.Add(genome);
                        }

                        // Set the modification bits for the two species.
                        updateBits[speciesIdx] = true;
                        updateBits[nearestSpeciesIdx] = true;

                        // Track the number of re-allocations.
                        Interlocked.Increment(ref reallocCount);
                    }
                });
            });

            // Complete moving of genomes to their new species.
            Parallel.ForEach(speciesArr, _parallelOptions, (species) => species.CompletePendingMoves());

            // Recalc the species centroids for species that have been modified.
            RecalcCentroids_GenomeById(speciesArr, updateBits);

            return reallocCount;
        }

        #endregion

        #region Private Methods [Regularization Helper Methods]

        /// <summary>
        /// Determine which species the given genome belongs to.
        /// </summary>
        private int DetermineGenomeSpecies(
            NeatGenome<T> genome,
            Species<T>[] speciesArr,
            double populationCount,
            double maxIntraSpeciesDistance)
        {
            int nearestSpeciesIdx = 0;
            double nearestDistance = GetAdjustedDistance(genome, speciesArr[0], populationCount, maxIntraSpeciesDistance);

            for(int i=1; i < speciesArr.Length; i++)
            {
                double distance = GetAdjustedDistance(genome, speciesArr[i], populationCount, maxIntraSpeciesDistance);
                if(distance < nearestDistance)
                {
                    nearestSpeciesIdx = i;
                    nearestDistance = distance;
                }
            }
            return nearestSpeciesIdx;

        }

        /// <summary>
        /// Gets an adjusted distance between the given genome and a species centroid.
        /// The adjustment introduces a regularization term.
        /// </summary>
        private double GetAdjustedDistance(
            NeatGenome<T> genome,
            Species<T> species,
            double populationCount,
            double maxIntraSpeciesDistance)
        {
            double distance = _distanceMetric.GetDistance(genome.ConnectionGenes, species.Centroid);

            // Calc regularization term.
            double clusterCount = species.GenomeById.Count;
            double r = (clusterCount / populationCount) * maxIntraSpeciesDistance * _regularizationConstant;

            // Return the distance plus the regularization term.
            return distance + r;
        }

        #endregion

        #region Private Methods [KMeans Helper Methods]

        private void KMeansInit(Species<T>[] speciesArr)
        {
            // Transfer all genomes from GenomeList to GenomeById.
            // Notes. moving genomes between species is more efficient when using dictionaries; 
            // removal from a list can have O(N) complexity because removing an item from 
            // a list requires shuffling up of items to fill the gap.
            Parallel.ForEach(speciesArr, _parallelOptions, species => species.LoadWorkingDictionary());
        }

        private void KMeansInit(Species<T>[] speciesArr, out double populationCount, out double maxIntraSpeciesDistance)
        {
            // Calc max distance between any two species.
            maxIntraSpeciesDistance = GetMaxIntraSpeciesCentroidDistance(speciesArr);

            // Transfer all genomes from GenomeList to GenomeById.
            // Notes. moving genomes between species is more efficient when using dictionaries;
            // removal from a list can have O(N) complexity because removing an item from 
            // a list requires shuffling up of items to fill the gap.
            int popCount = 0;

            Parallel.ForEach(speciesArr, _parallelOptions, (species) =>
            {
                Interlocked.Add(ref popCount, species.GenomeList.Count);
                species.LoadWorkingDictionary();
            });

            populationCount = popCount;
        }

        private void KMeansComplete(Species<T>[] speciesArr)
        {
            // Check for empty species (this can happen with k-means), and if there are any then 
            // move genomes into those empty species.
            var emptySpeciesArr = speciesArr.Where(x => 0 == x.GenomeById.Count).ToArray();
            if(emptySpeciesArr.Length != 0) {
                SpeciationUtilsParallel.PopulateEmptySpecies(_distanceMetric, emptySpeciesArr, speciesArr);
            }

            // Transfer all genomes from GenomeById to GenomeList.
            Parallel.ForEach(speciesArr, _parallelOptions, species => species.FlushWorkingDictionary());
        }

        private void RecalcCentroids_GenomeById(Species<T>[] speciesArr, bool[] updateBits)
        {
            Parallel.ForEach(Enumerable.Range(0, speciesArr.Length).Where(i => updateBits[i]), _parallelOptions, (i) =>
            {
                var species = speciesArr[i];
                species.Centroid = _distanceMetric.CalculateCentroid(species.GenomeById.Values.Select(x => x.ConnectionGenes)); 
            });
        }

        private void RecalcCentroids_GenomeList(Species<T>[] speciesArr, bool[] updateBits)
        {
            Parallel.ForEach(Enumerable.Range(0, speciesArr.Length).Where(i => updateBits[i]), _parallelOptions, (i) =>
            {
                var species = speciesArr[i];
                species.Centroid = _distanceMetric.CalculateCentroid(species.GenomeList.Select(x => x.ConnectionGenes)); 
            });
        }

        #endregion

        #region Private Methods [Regularization]

        private void GetPopulationCountAndMaxIntraSpeciesDistance(Species<T>[] speciesArr, out double populationCount, out double maxIntraSpeciesDistance)
        {
            // Calc max distance between any two species.
            maxIntraSpeciesDistance = GetMaxIntraSpeciesCentroidDistance(speciesArr);

            // Transfer all genomes from GenomeList to GenomeById.
            // Notes. moving genomes between species is more efficient when using dictionaries;
            // removal from a list can have O(N) complexity because removing an item from 
            // a list requires shuffling up of items to fill the gap.
            populationCount = 0;

            foreach(var species in speciesArr) {
                populationCount += species.GenomeList.Count;
            }
        }

        /// <summary>
        /// Calc the maximum distance between any two centroids.
        /// </summary>
        private double GetMaxIntraSpeciesCentroidDistance(Species<T>[] speciesArr)
        {
            double maxDistance = 0.0;

            // Iterate through all combinations of species, except for pairs of the same species.
            // Thus for N species the number of comparisons is N^2 - N.
            Parallel.For(0, speciesArr.Length - 1, _parallelOptions, (i) =>
            {
                var species = speciesArr[i];

                for(int j = i+1; j < speciesArr.Length; j++)
                {
                    double distance = _distanceMetric.GetDistance(species.Centroid, speciesArr[j].Centroid);
                    maxDistance = Math.Max(maxDistance, distance);
                }
            });

            return maxDistance;
        }

        #endregion
    }
}
