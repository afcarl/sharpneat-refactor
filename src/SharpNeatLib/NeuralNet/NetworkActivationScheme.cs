﻿
namespace SharpNeat.NeuralNet
{
    /// <summary>
    /// Represents network activation schemes.
    /// </summary>
    public class NetworkActivationScheme
    {
        bool _acyclicNetwork;

        // Cyclic network specific
        int _timestepsPerActivation;

        #region Constructors

        /// <summary>
        /// Private constructor to restrict construction to static factory methods.
        /// </summary>
        private NetworkActivationScheme()
        {

        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets a value indicating whether the network is acyclic or not (cyclic).
        /// </summary>
        public bool AcyclicNetwork
        {
            get { return _acyclicNetwork; }
        }

        /// <summary>
        /// Gets a fixed number of activation timesteps.
        /// </summary>
        public int TimestepsPerActivation
        {
            get { return _timestepsPerActivation; }
        }

        #endregion

        #region Static Factory Methods

        /// <summary>
        /// Create an activation scheme for acyclic networks.
        /// </summary>
        public static NetworkActivationScheme CreateAcyclicScheme()
        {
            NetworkActivationScheme scheme = new NetworkActivationScheme();
            scheme._acyclicNetwork = true;
            return scheme;
        }

        /// <summary>
        /// Create an activation scheme with a fixed number of activation timesteps (suitable for cyclic networks only).
        /// </summary>
        public static NetworkActivationScheme CreateCyclicScheme(int timestepsPerActivation)
        {
            NetworkActivationScheme scheme = new NetworkActivationScheme();
            scheme._acyclicNetwork = false;
            scheme._timestepsPerActivation = timestepsPerActivation;
            return scheme;
        }

        #endregion
    }
}
