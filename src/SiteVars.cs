//  Authors:  Robert M. Scheller, Alec Kretchun, Vincent Schuster

using Landis.Library.AgeOnlyCohorts;
using Landis.SpatialModeling;

namespace Landis.Extension.Scrapple
{
    public static class SiteVars
    {
        private static ISiteVar<FireEvent> eventVar;
        private static ISiteVar<int> timeOfLastFire;
        //private static ISiteVar<int> percentConifer;  //RMS: Maybe useful?
        //private static ISiteVar<int> percentHardwood; //RMS: Maybe useful?
        //private static ISiteVar<int> percentDeadFir;  //RMS: Maybe useful?
        //added for scrapple: ---
        private static ISiteVar<double> lightningFireWeight;
        private static ISiteVar<double> rxFireWeight;
        private static ISiteVar<double> accidentalFireWeight;
        private static ISiteVar<byte> typeOfIginition;
        //private static ISiteVar<bool> burned;
        private static ISiteVar<Site> originSite;
        // --------------End addgit 
        private static ISiteVar<byte> lastSeverity;
        private static ISiteVar<bool> disturbed;
        private static ISiteVar<ushort> groundSlope;
        private static ISiteVar<ushort> uphillSlopeAzimuth;

        private static ISiteVar<ushort> siteWindSpeed;  ////RMS: why?
        private static ISiteVar<ushort> siteWindDirection;  //RMS: why? 

        private static ISiteVar<ISiteCohorts> cohorts;

        //---------------------------------------------------------------------

        public static void Initialize()
        {

            cohorts = PlugIn.ModelCore.GetSiteVar<ISiteCohorts>("Succession.AgeCohorts");
            
            eventVar             = PlugIn.ModelCore.Landscape.NewSiteVar<FireEvent>(InactiveSiteMode.DistinctValues);
            timeOfLastFire       = PlugIn.ModelCore.Landscape.NewSiteVar<int>();
            //percentDeadFir       = PlugIn.ModelCore.Landscape.NewSiteVar<int>();
            lastSeverity         = PlugIn.ModelCore.Landscape.NewSiteVar<byte>();
            
            groundSlope          = PlugIn.ModelCore.Landscape.NewSiteVar<ushort>();
            uphillSlopeAzimuth   = PlugIn.ModelCore.Landscape.NewSiteVar<ushort>();
            siteWindSpeed        = PlugIn.ModelCore.Landscape.NewSiteVar<ushort>();
            siteWindDirection    = PlugIn.ModelCore.Landscape.NewSiteVar<ushort>();

            // Added for scrapple:
            lightningFireWeight  = PlugIn.ModelCore.Landscape.NewSiteVar<double>();
            rxFireWeight = PlugIn.ModelCore.Landscape.NewSiteVar<double>();
            accidentalFireWeight = PlugIn.ModelCore.Landscape.NewSiteVar<double>();
            typeOfIginition = PlugIn.ModelCore.Landscape.NewSiteVar<byte>();
            originSite  = PlugIn.ModelCore.Landscape.NewSiteVar<Site>();
            disturbed = PlugIn.ModelCore.Landscape.NewSiteVar<bool>();

            //Also initialize topography, will be overwritten if optional parameters provided:
            SiteVars.GroundSlope.ActiveSiteValues = 0;
            SiteVars.UphillSlopeAzimuth.ActiveSiteValues = 0;

            //Initialize TimeSinceLastFire to the maximum cohort age:
            foreach (ActiveSite site in PlugIn.ModelCore.Landscape)
            {
                ushort maxAge = GetMaxAge(site);
                timeOfLastFire[site] = PlugIn.ModelCore.StartTime - maxAge;
            }


            //PlugIn.ModelCore.RegisterSiteVar(SiteVars.FireRegion, "Fire.FireRegion");
            //PlugIn.ModelCore.RegisterSiteVar(SiteVars.FireRegion2, "Fire.FireRegion2");
            PlugIn.ModelCore.RegisterSiteVar(SiteVars.LastSeverity, "Fire.Severity");
            PlugIn.ModelCore.RegisterSiteVar(SiteVars.TimeOfLastFire, "Fire.TimeOfLastEvent");
        }

        //---------------------------------------------------------------------

        //public static void InitializeFuelType()
        //{
        //    //PlugIn.ModelCore.UI.WriteLine("   Initializing Fuel Type.");

        //    //cfsFuelType     = PlugIn.ModelCore.GetSiteVar<int>("Fuels.CFSFuelType");
        //    //cfsFuelType2    = PlugIn.ModelCore.GetSiteVar<int>("Fuels.CFSFuelType");
        //    //decidFuelType   = PlugIn.ModelCore.GetSiteVar<int>("Fuels.DecidFuelType");
        //    percentConifer  = PlugIn.ModelCore.GetSiteVar<int>("Fuels.PercentConifer");
        //    //percentHardwood = PlugIn.ModelCore.GetSiteVar<int>("Fuels.PercentHardwood");
        //    //percentDeadFir  = PlugIn.ModelCore.GetSiteVar<int>("Fuels.PercentDeadFir");

        //    //if (SiteVars.CFSFuelType == null)
        //    //    throw new System.ApplicationException("Error: CFS Fuel Type NOT Initialized.  Fuel extension MUST be active.");

        //    //SiteVars.PercentDeadFir.ActiveSiteValues = 0;

        //}
        ////---------------------------------------------------------------------
        // Added for Scrapple:
        ////---------------------------------------------------------------------
        public static ISiteVar<double> LightningFireWeight
        {
            get
            {
                return lightningFireWeight;
            }
        }
        ////---------------------------------------------------------------------
        public static ISiteVar<double> RxFireWeight
        {
            get
            {
                return rxFireWeight;
            }
        }
        ////---------------------------------------------------------------------
        public static ISiteVar<double> AccidentalFireWeight
        {
            get
            {
                return accidentalFireWeight;
            }
        }

        public static ISiteVar<byte> TypeOfIginition
        {
            get
            {
                return typeOfIginition;
            }
        }

        public static ISiteVar<Site> OriginSite
        {
            get
            {
                return originSite;
            }

            set
            {
                originSite = value;
            }
        }
        // ------------ End addition
        

        public static ISiteVar<int> TimeOfLastFire
        {
            get {
                return timeOfLastFire;
            }
        }
        //---------------------------------------------------------------------
        public static ISiteVar<FireEvent> FireEvent
        {
            get {
                return eventVar;
            }
        }
        
        //public static ISiteVar<int> PercentConifer
        //{
        //    get {
        //        return percentConifer;
        //    }
        //}

        ////---------------------------------------------------------------------

        //public static ISiteVar<int> PercentHardwood
        //{
        //    get {
        //        return percentHardwood;
        //    }
        //}
        ////---------------------------------------------------------------------

        //public static ISiteVar<int> PercentDeadFir
        //{
        //    get {
        //        return percentDeadFir;
        //    }
        //}

        //---------------------------------------------------------------------
        public static ISiteVar<byte> LastSeverity
        {
            get
            {
                return lastSeverity;
            }
        }

        //---------------------------------------------------------------------

        public static ISiteVar<bool> Disturbed
        {
            get {
                return disturbed;
            }
        }
        //---------------------------------------------------------------------
        
        public static ISiteVar<ushort> GroundSlope
        {
            get {
                return groundSlope;
            }
        }
        //---------------------------------------------------------------------

        public static ISiteVar<ushort> UphillSlopeAzimuth
        {
            get {
                return uphillSlopeAzimuth;
            }
        }
        //---------------------------------------------------------------------
        public static ISiteVar<ushort> SiteWindSpeed
        {
            get
            {
                return siteWindSpeed;
            }
        }

        //---------------------------------------------------------------------
        public static ISiteVar<ushort> SiteWindDirection
        {
            get
            {
                return siteWindDirection;
            }
        }

        //---------------------------------------------------------------------

        public static ISiteVar<ISiteCohorts> Cohorts
        {
            get
            {
                return cohorts;
            }
        }

        //---------------------------------------------------------------------
        public static ushort GetMaxAge(ActiveSite site)
        {
            if (SiteVars.Cohorts[site] == null)
            {
                PlugIn.ModelCore.UI.WriteLine("Cohort are null.");
                return 0;
            }
            ushort max = 0;

            foreach (ISpeciesCohorts speciesCohorts in SiteVars.Cohorts[site])
            {
                foreach (ICohort cohort in speciesCohorts)
                {
                    if (cohort.Age > max)
                        max = cohort.Age;
                }
            }
            return max;
        }
    }
}
