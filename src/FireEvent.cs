//  Authors:  Robert M. Scheller, Alec Kretchun, Vincent Schuster

using Landis.Library.AgeOnlyCohorts;
using Landis.SpatialModeling;
using Landis.Core;
using Landis.Library.Climate;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;

namespace Landis.Extension.Scrapple
{

    public enum Ignition
    {
        Accidental,
        Lightning,
        Rx,
        Spread
    }

    public class FireEvent
        : ICohortDisturbance
    {
        private static readonly bool isDebugEnabled = false; //debugLog.IsDebugEnabled;
        public static Random rnd = new Random();

        private ActiveSite initiationSite;
        private int totalSitesDamaged;

        private int cohortsKilled;
//        private double eventSeverity;
        
        public double InitiationFireWeatherIndex;
        public Ignition IgnitionType;
        AnnualClimate_Daily annualWeatherData;
        public int NumberOfDays;
        public double MeanSeverity;
        public double MeanWindDirection;
        public double MeanWindSpeed;
        public double MeanSuppression;
        public double TotalBiomassMortality;
        public int NumberCellsSeverity1;
        public int NumberCellsSeverity2;
        public int NumberCellsSeverity3;

        public Dictionary<int, int> spreadArea;

        public int maxDay;

        //---------------------------------------------------------------------
        static FireEvent()
        {
        }
        //---------------------------------------------------------------------

        public int TotalSitesDamaged
        {
            get {
                return totalSitesDamaged;
            }
        }
        //---------------------------------------------------------------------

        public int CohortsKilled
        {
            get {
                return cohortsKilled;
            }
        }

        //---------------------------------------------------------------------

        ExtensionType IDisturbance.Type
        {
            get {
                return PlugIn.ExtType;
            }
        }


        //---------------------------------------------------------------------
        ActiveSite IDisturbance.CurrentSite
        {
            get
            {
                return initiationSite;
            }
        }
        // Constructor function

        public FireEvent(ActiveSite initiationSite, int day, Ignition ignitionType)
        {
            this.initiationSite = initiationSite;
            this.IgnitionType = ignitionType;
            IEcoregion ecoregion = PlugIn.ModelCore.Ecoregion[initiationSite];

            int actualYear = (PlugIn.ModelCore.CurrentTime - 1) + Climate.Future_DailyData.First().Key;
            this.annualWeatherData = Climate.Future_DailyData[actualYear][ecoregion.Index];
            SiteVars.TypeOfIginition[initiationSite] = (byte)ignitionType;
            SiteVars.Disturbed[initiationSite] = true;

            this.cohortsKilled = 0;
            this.totalSitesDamaged = 0;
            this.InitiationFireWeatherIndex = annualWeatherData.DailyFireWeatherIndex[day];
            this.spreadArea = new Dictionary<int, int>();
            this.NumberOfDays = 0;
            this.MeanSeverity = 0.0;
            this.MeanWindDirection = 0.0;
            this.MeanWindSpeed = 0.0;
            this.MeanSuppression = 0.0;
            this.TotalBiomassMortality = 0.0;
            this.NumberCellsSeverity1 = 0;
            this.NumberCellsSeverity2 = 0;
            this.NumberCellsSeverity3 = 0;
            this.maxDay = day;

        //this.windSpeed = annualWeatherData.DailyWindSpeed[day];
        //this.windDirection = annualWeatherData.DailyWindDirection[day];
        //this.originLocation = initiationSite.Location;
    }


    //---------------------------------------------------------------------
    public static FireEvent Initiate(ActiveSite initiationSite, int timestep, int day, Ignition ignitionType)

        {
            PlugIn.ModelCore.UI.WriteLine("  Fire Event initiated.  Day = {0}, IgnitionType = {1}.", day, ignitionType);
            //double randomNum = PlugIn.ModelCore.GenerateUniform();
            
            //First, check for fire overlap (NECESSARY??):

            if (!SiteVars.Disturbed[initiationSite])
            {
                // Randomly select neighbor to spread to
                if (isDebugEnabled)
                    PlugIn.ModelCore.UI.WriteLine("   Fire event started at {0} ...", initiationSite.Location);

                FireEvent fireEvent = new FireEvent(initiationSite, day, ignitionType);

                fireEvent.Spread(PlugIn.ModelCore.CurrentTime, day, (ActiveSite) initiationSite);
                if(fireEvent.CohortsKilled > 0)
                    LogEvent(PlugIn.ModelCore.CurrentTime, fireEvent);

                return fireEvent;


            }
            else
            {
                return null;
            }
        }

        

        //---------------------------------------------------------------------
        public void Spread(int currentTime, int day, ActiveSite site)
        {
            // First, load necessary parameters
            //      load fwi
            //      load wind speed velocity (in which case, NOT a fire event parameter)
            //      load wind direction (in which case, NOT a fire event parameter)
            //      load fine fuels
            //      load uphill slope azimuth
            //      wind speed = wind speed adjusted
            //AMK equations for wind speed/direction factor conversions from raw data 
            //Refer to design doc on Google Drive for questions or explanations
            //wsx = (wind_speed_velocity * sin(fire_azimuth)) + (wind_speed_velocity * sin(uphill_azimuth))
            //wsy = (wind_speed_velocity * cos(fire_azimuth)) + (wind_speed_velocity * cos(uphill_azimuth))
            //ws.factor = sqrt(wsx^2 + wsy^2) //wind speed factor
            //wd.factor = acos(wsy/ws.factor) //wind directior factor

            if (day > maxDay)
            {
                maxDay = day;
                NumberOfDays++;
            }

            IEcoregion ecoregion = PlugIn.ModelCore.Ecoregion[site];

            double fireWeatherIndex = 0.0;
            try
            {
                fireWeatherIndex = this.annualWeatherData.DailyFireWeatherIndex[day]; 
            }
            catch
            {
                throw new UninitializedClimateData(string.Format("Fire Weather Index could not be found \t year: {0}, day: {1} in ecoregion: {2} not found", currentTime, day, ecoregion.Name));
            }
            double windSpeed = this.annualWeatherData.DailyWindSpeed[day];
            double windDirection = this.annualWeatherData.DailyWindDirection[day];
            this.MeanWindDirection += windDirection;
            this.MeanWindSpeed += windSpeed;
            //double fineFuels = SiteVars.FineFuels[site];  // NEED TO FIX NECN-Hydro installer
            PlugIn.ModelCore.UI.WriteLine("  Fire spreading.  Day = {0}, FWI = {1}, windSpeed = {2}, windDirection = {3}.", day, fireWeatherIndex, windSpeed, windDirection);

            // Is spread to this site allowable?
            //          Calculate P-spread based on fwi, adjusted wind speed, fine fuels, source intensity (or similar). (AK)
            //          Adjust P-spread to account for suppression (RMS)
            //          Compare P-spread-adj to random number

            // ********* TEMP ****************************************
            double Pspread_adjusted = 0.05;
            // ********* TEMP ****************************************

            if (Pspread_adjusted > PlugIn.ModelCore.GenerateUniform())
            {
                SiteVars.Disturbed[site] = true;  // set to true, regardless of severity


                // Next, determine severity (0 = none, 1 = <4', 2 = 4-8', 3 = >8'.
                //      Severity a function of fwi, ladder fuels, other? (AK)
                // ********* TEMP ****************************************
                int severity = (int) Math.Ceiling(PlugIn.ModelCore.GenerateUniform() * 3.0);
                // ********* TEMP ****************************************
                int siteCohortsKilled = 0;

                if (severity > 0)
                {
                    //      Cause mortality
                    siteCohortsKilled = Damage(site);
                    if (siteCohortsKilled > 0)
                    {
                        this.totalSitesDamaged++;
                    }

                    // Log information
                    SiteVars.TypeOfIginition[site] = (byte)this.IgnitionType;
                    SiteVars.Severity[site] = (byte) severity;
                    SiteVars.DayOfFire[site] = (byte) day;
                    this.MeanSeverity += severity;
                    if (severity == 1)
                        this.NumberCellsSeverity1++;
                    if (severity == 2)
                        this.NumberCellsSeverity2++;
                    if (severity == 3)
                        this.NumberCellsSeverity3++;

                }

                //      Calculate spread-area-max (AK)  TODO
                // ********* TEMP ****************************************
                int spreadAreaMax = 3;
                // ********* TEMP ****************************************
                if (!spreadArea.ContainsKey(day))
                {
                    spreadArea.Add(day, 1);  // second int is the cell count, later turned into area
                }
                else
                {
                    spreadArea[day]++;
                }

                //      Spread to neighbors
                List<Site> neighbors = Get4ActiveNeighbors(initiationSite);
                neighbors.RemoveAll(neighbor => SiteVars.Disturbed[neighbor] || !neighbor.IsActive);
                int neighborDay = day;


                foreach (Site neighborSite in neighbors)
                {
                    //  if spread-area > spread-area-max, day = day + 1
                    if (spreadArea[day] > spreadAreaMax)
                        neighborDay = day+1;
                    this.Spread(PlugIn.ModelCore.CurrentTime, neighborDay, (ActiveSite)initiationSite);
                }

                // if there are no neighbors already disturbed then nothing to do since it can't spread
                //if (neighbors.Count > 0)
                //{
                //    // VS: for now pick random site to spread to
                //    // RMS TODO:  Spread to all neighbors
                //    int r = rnd.Next(neighbors.Count);
                //    Site nextSite = neighbors[r];

                //    //Initiate a fireevent at that site
                //    //FireEvent spreadEvent = Initiate((ActiveSite)nextSite, currentTime, day, Ignition.Spread, (this.SpreadLength - 1));
                //    //if (fireEvent.SpreadDistance > 0)
                //    //{

                //    //}
                //}


            }



        }

        //---------------------------------------------------------------------
        private static List<Site> Get4ActiveNeighbors(Site srcSite)
        {
            if (!srcSite.IsActive)
                throw new ApplicationException("Source site is not active.");

            List<Site> neighbors = new List<Site>();

            RelativeLocation[] neighborhood = new RelativeLocation[]
            {
                new RelativeLocation(-1,  0),  // north
                new RelativeLocation( 0,  1),  // east
                new RelativeLocation( 1,  0),  // south
                new RelativeLocation( 0, -1),  // west
            };

            foreach (RelativeLocation relativeLoc in neighborhood)
            {
                Site neighbor = srcSite.GetNeighbor(relativeLoc);

                if (neighbor != null && neighbor.IsActive)
                {
                    neighbors.Add(neighbor);
                }
            }

            return neighbors; //fastNeighbors;
        }
        //---------------------------------------------------------------------

        private int Damage(ActiveSite site)
        {
            int previousCohortsKilled = this.cohortsKilled;
            SiteVars.Cohorts[site].RemoveMarkedCohorts(this);
            return this.cohortsKilled - previousCohortsKilled;
        }

        //---------------------------------------------------------------------

        //  A filter to determine which cohorts are removed.

        bool ICohortDisturbance.MarkCohortForDeath(ICohort cohort)
        {
            bool killCohort = false;
            int siteSeverity = 1;

            List<IFireDamage> fireDamages = null;
            if (siteSeverity == 1)
                fireDamages = PlugIn.FireDamages_Severity1;
            if (siteSeverity == 2)
                fireDamages = PlugIn.FireDamages_Severity2;
            if (siteSeverity == 3)
                fireDamages = PlugIn.FireDamages_Severity3;

            foreach (IFireDamage damage in fireDamages)
            {
                if(cohort.Species == damage.DamageSpecies && cohort.Age >= damage.MinAge && cohort.Age < damage.MaxAge)
                {
                    if (damage.ProbablityMortality > PlugIn.ModelCore.GenerateUniform())
                    {
                        killCohort = true;
                        // this.TotalBiomassMortality += cohort.Biomass;  RMS TODO Convert to biomass cohorts
                    }
                    break;  // No need to search further

                }
            }

            if (killCohort) {
                this.cohortsKilled++;
            }
            return killCohort;
        }

        //---------------------------------------------------------------------

        public static void LogEvent(int currentTime, FireEvent fireEvent)
        {

            PlugIn.eventLog.Clear();
            EventsLog el = new EventsLog();
            el.SimulationYear = currentTime;
            el.InitRow = fireEvent.initiationSite.Location.Row;
            el.InitColumn = fireEvent.initiationSite.Location.Column;
            el.InitialFireWeatherIndex = fireEvent.InitiationFireWeatherIndex;
            el.IgnitionType = fireEvent.IgnitionType.ToString();
            el.NumberOfDays = fireEvent.NumberOfDays;
            el.TotalSitesBurned = fireEvent.TotalSitesDamaged;
            el.CohortsKilled = fireEvent.CohortsKilled;
            el.MeanSeverity = fireEvent.MeanSeverity / (double) fireEvent.TotalSitesDamaged;
            el.MeanWindDirection = fireEvent.MeanWindDirection / (double)fireEvent.TotalSitesDamaged;
            el.MeanWindSpeed = fireEvent.MeanWindSpeed / (double)fireEvent.TotalSitesDamaged;
            el.MeanSuppression = fireEvent.MeanSuppression / (double)fireEvent.TotalSitesDamaged;
            el.TotalBiomassMortality = fireEvent.TotalBiomassMortality;
            el.NumberCellsSeverity1 = fireEvent.NumberCellsSeverity1;
            el.NumberCellsSeverity2 = fireEvent.NumberCellsSeverity2;
            el.NumberCellsSeverity3 = fireEvent.NumberCellsSeverity3;

            PlugIn.eventLog.AddObject(el);
            PlugIn.eventLog.WriteToFile();

        }

        //---------------------------------------------------------------------
        /// <summary>
        /// Compares weights
        /// </summary>

        public class WeightComparer : IComparer<WeightedSite>
        {
            public int Compare(WeightedSite x,
                                              WeightedSite y)
            {
                int myCompare = x.Weight.CompareTo(y.Weight);
                return myCompare;
            }

        }

        private static double CalculateSF(int groundSlope)
        {
            return Math.Pow(Math.E, 3.533 * Math.Pow(((double)groundSlope / 100),1.2));  //FBP 39
        }

    }


    public class WeightedSite
    {
        private Site site;
        private double weight;

        //---------------------------------------------------------------------
        public Site Site
        {
            get {
                return site;
            }
            set {
                site = value;
            }
        }

        public double Weight
        {
            get {
                return weight;
            }
            set {
                weight = value;
            }
        }

        public WeightedSite (Site site, double weight)
        {
            this.site = site;
            this.weight = weight;
        }

    }
}
