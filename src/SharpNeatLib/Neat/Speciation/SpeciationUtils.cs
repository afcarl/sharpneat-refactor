﻿using System;
using System.Linq;
using SharpNeat.Neat.DistanceMetrics;
using SharpNeat.Neat.Genome;

namespace SharpNeat.Neat.Speciation
{
    /// <summary>
    /// Static utility methods related to speciation.
    /// </summary>
    public static class SpeciationUtils
    {
        #region Public Static Methods [IDistanceMetric Utility Methods]

        /// <summary>
        /// Get the index of the species with a centroid that is nearest to the provided genome.
        /// </summary>
        public static int GetNearestSpecies<T>(
            IDistanceMetric<T> distanceMetric,
            NeatGenome<T> genome,
            Species<T>[] speciesArr)
        where T : struct
        {
            int nearestSpeciesIdx = 0;
            double nearestDistance = distanceMetric.GetDistance(genome.ConnectionGenes, speciesArr[0].Centroid);

            for(int i=1; i < speciesArr.Length; i++)
            {
                double distance = distanceMetric.GetDistance(genome.ConnectionGenes, speciesArr[i].Centroid);
                if(distance < nearestDistance)
                {
                    nearestSpeciesIdx = i;
                    nearestDistance = distance;
                }
            }
            return nearestSpeciesIdx;
        }

        #endregion

        #region Public Methods [Empty Species Handling]

        public static void PopulateEmptySpecies<T>(
            IDistanceMetric<T> distanceMetric,
            Species<T>[] emptySpeciesArr,
            Species<T>[] speciesArr)
        where T : struct
        {
            foreach(Species<T> emptySpecies in emptySpeciesArr)
            {
                // Get and remove a genome from a species with many genomes.
                var genome = GetGenomeForEmptySpecies(distanceMetric, speciesArr);

                // Add the genome to the empty species.
                emptySpecies.GenomeById.Add(genome.Id, genome);

                // Update the centroid. There's only one genome so it is the centroid.
                emptySpecies.Centroid = genome.ConnectionGenes;
            }
        }

        private static NeatGenome<T> GetGenomeForEmptySpecies<T>(
            IDistanceMetric<T> distanceMetric,
            Species<T>[] speciesArr)
        where T : struct
        {
            // Get the species with the highest number of genomes.
            Species<T> species = speciesArr.Aggregate((x, y) => x.GenomeById.Count > y.GenomeById.Count ?  x : y);

            // Get the genome furthest from the species centroid.
            var genome = species.GenomeById.Values.Aggregate((x, y) => distanceMetric.GetDistance(species.Centroid, x.ConnectionGenes) > distanceMetric.GetDistance(species.Centroid, y.ConnectionGenes) ? x : y);

            // Remove the genome from its current species.
            species.GenomeById.Remove(genome.Id);

            // Update the species centroid.
            species.Centroid = distanceMetric.CalculateCentroid(species.GenomeById.Values.Select(x => x.ConnectionGenes));

            // Return the selected genome.
            return genome;
        }

        #endregion
    }
}
