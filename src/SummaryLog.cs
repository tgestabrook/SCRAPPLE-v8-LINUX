﻿//  Authors:  Robert M. Scheller, Alec Kretchun, Vincent Schuster

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Landis.Library.Metadata;

namespace Landis.Extension.Scrapple
{
    public class SummaryLog
    {

        [DataFieldAttribute(Unit = FieldUnits.Year, Desc = "Simulation Year")]
        public int SimulationYear {set; get;}

        [DataFieldAttribute(Unit = FieldUnits.Count, Desc = "Total Sites Burned")]
        public int TotalBurnedSites { set; get; }

        [DataFieldAttribute(Unit = FieldUnits.Count, Desc = "Number of Fires")]
        public int NumberFires { set; get; }

        [DataFieldAttribute(Unit = FieldUnits.g_B_m2, Desc = "Biomass Killed")]
        public int TotalBiomassMortality { set; get; }

        [DataFieldAttribute(Unit = FieldUnits.Count, Desc = "Number of Cells Severity 1")]
        public double NumberCellsSeverity1 { set; get; }

        [DataFieldAttribute(Unit = FieldUnits.Count, Desc = "Number of Cells Severity 2")]
        public double NumberCellsSeverity2 { set; get; }

        [DataFieldAttribute(Unit = FieldUnits.Count, Desc = "Number of Cells Severity 3")]
        public double NumberCellsSeverity3 { set; get; }

    }
}
