// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

// <auto-generated/>

#nullable disable

using System;

namespace Azure.Communication.ShortCodes.Models
{
    /// <summary> Holds a note about a Program Brief that has gone thru stages of review process. </summary>
    public partial class ReviewNote
    {
        /// <summary> Initializes a new instance of ReviewNote. </summary>
        public ReviewNote()
        {
        }

        /// <summary> Initializes a new instance of ReviewNote. </summary>
        /// <param name="message"> Note related to a Program Brief that may imply changes needed from the client. </param>
        /// <param name="date"> Date and time when the note was added to the Program Brief. </param>
        internal ReviewNote(string message, DateTimeOffset? date)
        {
            Message = message;
            Date = date;
        }

        /// <summary> Note related to a Program Brief that may imply changes needed from the client. </summary>
        public string Message { get; set; }
        /// <summary> Date and time when the note was added to the Program Brief. </summary>
        public DateTimeOffset? Date { get; set; }
    }
}
