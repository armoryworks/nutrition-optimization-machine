// File: Nom.Api/_Abstractions/_Core/IErrorHandler.cs

using System;
using System.Threading.Tasks;
using System.Collections.Generic; // Added for Dictionary

namespace Nom.Api.Core
{
    /// <summary>
    /// Error severity levels
    /// </summary>
    public enum ErrorSeverityEnum
    {
        /// <summary>
        /// Information level
        /// </summary>
        Information,

        /// <summary>
        /// Warning level
        /// </summary>
        Warning,

        /// <summary>
        /// Error level
        /// </summary>
        Error,

        /// <summary>
        /// Critical level
        /// </summary>
        Critical,

        /// <summary>
        /// Fatal level
        /// </summary>
        Fatal
    }
}