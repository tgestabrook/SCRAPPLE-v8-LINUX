//  Authors:  Robert M. Scheller, Alec Kretchun, Vincent Schuster

using Landis.SpatialModeling;
using Landis.Library.Climate;
using Landis.Core;
using Landis.Library.Metadata;
using Ether.WeightedSelector;  

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace Landis.Extension.Scrapple
{
    ///<summary>
    /// A disturbance plug-in that simulates Fire disturbance.
    /// </summary>
    public class PlugIn
        : ExtensionMain 
    {
        private static readonly bool isDebugEnabled = true; 

        public static readonly ExtensionType ExtType = new ExtensionType("disturbance:fire");
        public static readonly string ExtensionName = "SCRAPPLE";
        public static MetadataTable<EventsLog> eventLog;
        public static MetadataTable<SummaryLog> summaryLog;
        public static MetadataTable<IgnitionsLog> ignitionsLog;
        
        // Get the active sites from the landscape and shuffle them 
        public List<ActiveSite> activeRxSites; 
        public List<ActiveSite> activeAccidentalSites;
        public List<ActiveSite> activeLightningSites;
        public double rxTotalWeight;
        public double accidentalTotalWeight;
        public double lightningTotalWeight;

        // RMS Testing 8/2019
        public WeightedSelector<ActiveSite> weightedRxSites;
        public WeightedSelector<ActiveSite> weightedAccidentalSites;
        public WeightedSelector<ActiveSite> weightedLightningSites;


        public static int FutureClimateBaseYear;
        public static Dictionary<int, int> sitesPerClimateRegion;
        public static Dictionary<int, double> fractionSitesPerClimateRegion;
        public static int ActualYear;
        public static int EventID = 0;

        private static int[] dNBR;
        private static int[] totalBurnedSites;
        private static int[] numberOfFire;
        private static int[] totalBiomassMortality;
        private static int numCellsSeverity1;
        private static int numCellsSeverity2;
        private static int numCellsSeverity3;
        private static int numCellsSeverity4;
        private static int numCellsSeverity5;

        public static IInputParameters Parameters;
        private static ICore modelCore;

        public static double MaximumSpreadAreaB0;
        public static double MaximumSpreadAreaB1;
        public static double MaximumSpreadAreaB2;

        public static int DaysPerYear = 364;

        private List<IDynamicIgnitionMap> dynamicRxIgns;
        private List<IDynamicIgnitionMap> dynamicLightningIgns;
        private List<IDynamicIgnitionMap> dynamicAccidentalIgns;
        private List<IDynamicSuppressionMap> dynamicSuppress;

        public static IgnitionDistribution IgnitionDist = IgnitionDistribution.Poisson;

        //---------------------------------------------------------------------

        public PlugIn()
            : base(ExtensionName, ExtType)
        {
        }

        //---------------------------------------------------------------------

        public static ICore ModelCore
        {
            get
            {
                return modelCore;
            }
        }
        
        //---------------------------------------------------------------------

        public override void LoadParameters(string dataFile,
                                            ICore mCore)
        {
            modelCore = mCore;
            SiteVars.Initialize();
            InputParameterParser parser = new InputParameterParser();
            Parameters = Landis.Data.Load<IInputParameters>(dataFile, parser);
        }

        //---------------------------------------------------------------------

        public override void Initialize()
        {
            Timestep = 1;  // RMS:  Initially we will force annual time step. parameters.Timestep;


            ///******************** DEBUGGER LAUNCH *********************
            /// 
            /*
            if (Debugger.Launch())
            {
                modelCore.UI.WriteLine("Debugger is attached");
                if (Debugger.IsLogging())
                {
                    modelCore.UI.WriteLine("Debugging is logging");
                }
                Debugger.Break();
            }
            else
            { 
                modelCore.UI.WriteLine("Debugger not attached");
            }
            */
            ///******************** DEBUGGER END *********************

            modelCore.UI.WriteLine("Initializing SCRAPPLE Fire...");

            SpeciesData.Initialize(Parameters);
            dynamicRxIgns = Parameters.DynamicRxIgnitionMaps;
            dynamicLightningIgns    = Parameters.DynamicLightningIgnitionMaps;
            dynamicAccidentalIgns   = Parameters.DynamicAccidentalIgnitionMaps;
            dynamicSuppress = Parameters.DynamicSuppressionMaps;
            MapUtility.Initilize(Parameters.LighteningFireMap, Parameters.AccidentalFireMap, Parameters.RxFireMap,
                                 Parameters.LighteningSuppressionMap, Parameters.AccidentalSuppressionMap, Parameters.RxSuppressionMap);
            if (Parameters.RxZonesMap != null)
                MapUtility.ReadMap(Parameters.RxZonesMap, SiteVars.RxZones);
            MetadataHandler.InitializeMetadata(Parameters.Timestep, ModelCore);

            sitesPerClimateRegion = new Dictionary<int, int>();

            foreach (ActiveSite site in PlugIn.ModelCore.Landscape)
            {
                IEcoregion ecoregion = PlugIn.ModelCore.Ecoregion[site];
                if (!sitesPerClimateRegion.ContainsKey(ecoregion.Index))
                    sitesPerClimateRegion.Add(ecoregion.Index, 1);
                else
                    sitesPerClimateRegion[ecoregion.Index]++;

            }

            fractionSitesPerClimateRegion = new Dictionary<int, double>();
            foreach (IEcoregion ecoregion in PlugIn.ModelCore.Ecoregions)
            {
                if (sitesPerClimateRegion.ContainsKey(ecoregion.Index))
                {
                    fractionSitesPerClimateRegion.Add(ecoregion.Index, ((double)sitesPerClimateRegion[ecoregion.Index] / (double)modelCore.Landscape.ActiveSiteCount));
                }
            }

            SiteVars.TimeOfLastFire.ActiveSiteValues = -999;  // default value to distinguish from recent fires.

        }

        //---------------------------------------------------------------------

        ///<summary>
        /// Run the plug-in at a particular timestep.
        ///</summary>
        public override void Run()
        {

            if (PlugIn.ModelCore.CurrentTime > 0)
                SiteVars.InitializeDisturbances();

            SiteVars.Disturbed.ActiveSiteValues = false;
            SiteVars.Intensity.ActiveSiteValues = 0;
            SiteVars.SpreadProbability.ActiveSiteValues = 0.0;
            SiteVars.DayOfFire.ActiveSiteValues = 0;
            SiteVars.TypeOfIginition.ActiveSiteValues = 0;
            SiteVars.SpecialDeadWood.ActiveSiteValues = 0;
            SiteVars.EventID.ActiveSiteValues = 0;

            foreach (IDynamicIgnitionMap dynamicRxIgnitions in dynamicRxIgns)
            {
                if (dynamicRxIgnitions.Year == PlugIn.modelCore.CurrentTime)
                {
                    PlugIn.ModelCore.UI.WriteLine("   Reading in new Ignitions Maps {0}.", dynamicRxIgnitions.MapName);
                    MapUtility.ReadMap(dynamicRxIgnitions.MapName, SiteVars.RxFireWeight);

                    double totalWeight = 0.0;
                    activeRxSites = PreShuffle(SiteVars.RxFireWeight, out totalWeight);
                    rxTotalWeight = totalWeight;

                }

            }

            foreach (IDynamicIgnitionMap dynamicLxIgns in dynamicLightningIgns)
            {
                if (dynamicLxIgns.Year == PlugIn.modelCore.CurrentTime)
                {
                    PlugIn.ModelCore.UI.WriteLine("   Reading in new Ignitions Maps {0}.", dynamicLxIgns.MapName);
                    MapUtility.ReadMap(dynamicLxIgns.MapName, SiteVars.LightningFireWeight);

                    double totalWeight = 0.0;
                    activeLightningSites = PreShuffle(SiteVars.LightningFireWeight, out totalWeight);
                    lightningTotalWeight = totalWeight;

                }

            }
            foreach (IDynamicIgnitionMap dynamicAxIgns in dynamicAccidentalIgns)
            {
                if (dynamicAxIgns.Year == PlugIn.modelCore.CurrentTime)
                {
                    PlugIn.ModelCore.UI.WriteLine("   Reading in new Ignitions Maps {0}.", dynamicAxIgns.MapName);
                    MapUtility.ReadMap(dynamicAxIgns.MapName, SiteVars.AccidentalFireWeight);

                    double totalWeight = 0.0;
                    activeAccidentalSites = PreShuffle(SiteVars.AccidentalFireWeight, out totalWeight);
                    accidentalTotalWeight = totalWeight;

                }

            }
            foreach (IDynamicSuppressionMap dynamicSuppressMaps in dynamicSuppress)
            {
                if (dynamicSuppressMaps.Year == PlugIn.modelCore.CurrentTime)
                {
                    PlugIn.ModelCore.UI.WriteLine("   Reading in new Fire Suppression Map {0}.", dynamicSuppressMaps.MapName);
                    MapUtility.ReadMap(dynamicSuppressMaps.MapName, SiteVars.AccidentalSuppressionIndex);
                }
            }

            AnnualClimate_Daily weatherData = null;
            dNBR = new int[3];
            totalBurnedSites = new int[3];
            numberOfFire = new int[3];
            totalBiomassMortality = new int[3];
            numCellsSeverity1 = 0;
            numCellsSeverity2 = 0;
            numCellsSeverity3 = 0;

            modelCore.UI.WriteLine("   Processing landscape for Fire events ...");

            ActualYear = 0;
            try
            {
                ActualYear = (PlugIn.ModelCore.CurrentTime - 1) + Climate.Future_AllData.First().Key;
            }
            catch
            {
                throw new UninitializedClimateData(string.Format("Could not initilize the actual year {0} from climate data", ActualYear));
            }

            modelCore.UI.WriteLine("   Next, shuffle ignition sites...");
            // Get the active sites from the landscape and shuffle them 
            // Sites are weighted for ignition in the Ether.WeightedSelector Shuffle method, based on the respective inputs maps.
            int numSites = 0;
            weightedRxSites = PreShuffleEther(SiteVars.RxFireWeight, out numSites);
            int numRxSites = numSites;
            weightedAccidentalSites = PreShuffleEther(SiteVars.AccidentalFireWeight, out numSites);
            int numAccidentalSites = numSites;
            weightedLightningSites = PreShuffleEther(SiteVars.LightningFireWeight, out numSites);
            int numLightningSites = numSites;

            // Deprecated shuffling:
            //List < ActiveSite> shuffledLightningFireSites = Shuffle(activeLightningSites, SiteVars.LightningFireWeight, lightningTotalWeight);
            //List<ActiveSite> shuffledRxFireSites = Shuffle(activeRxSites, SiteVars.RxFireWeight, rxTotalWeight);
            //List<ActiveSite> shuffledAccidentalFireSites = Shuffle(activeAccidentalSites, SiteVars.AccidentalFireWeight, accidentalTotalWeight);

            modelCore.UI.WriteLine("   Next, loop through each day to start fires...");

            int numAnnualRxFires = Parameters.RxNumberAnnualFires;

            for (int day = 0; day < DaysPerYear; ++day)
            {
                double landscapeAverageFireWeatherIndex = 0.0;
                double landscapeAverageTemperature = 0.0;
                double landscapeAverageRelHumidity = 0.0;
                // number of fires get initilized to 0 every timestep

                foreach (IEcoregion climateRegion in PlugIn.ModelCore.Ecoregions)
                {
                    if (sitesPerClimateRegion.ContainsKey(climateRegion.Index))
                    {
                        //if (sitesPerEcoregions.ContainsKey(ecoregion.Index))
                        double climateRegionFractionSites = (double) fractionSitesPerClimateRegion[climateRegion.Index];

                        try
                        {
                            weatherData = Climate.Future_DailyData[ActualYear][climateRegion.Index];
                        }
                        catch
                        {
                            throw new UninitializedClimateData(string.Format("Climate data could not be found in Run(). Year: {0} in ecoregion: {1}", ActualYear, climateRegion.Name));
                        }

                        try
                        {
                            landscapeAverageFireWeatherIndex += weatherData.DailyFireWeatherIndex[day] * climateRegionFractionSites;
                            landscapeAverageTemperature += weatherData.DailyMaxTemp[day] * climateRegionFractionSites;
                            landscapeAverageRelHumidity += weatherData.DailyMinRH[day] * climateRegionFractionSites;
                           // modelCore.UI.WriteLine("  Fire Weather Check Daily={0}, Average={1}", weatherData.DailyFireWeatherIndex[day], landscapeAverageFireWeatherIndex);
                        }
                        catch
                        {
                            throw new UninitializedClimateData(string.Format("Fire Weather Index could not be found in Run(). Year: {0}, day: {1}, climate region: {2}, NumSites={3}", ActualYear, day, climateRegion.Name, sitesPerClimateRegion[climateRegion.Index]));
                        }

                        if (Climate.Future_DailyData[PlugIn.ActualYear][climateRegion.Index].DailyRH[day] < 0)
                        {
                            string mesg = string.Format("Relative Humidity not included in the climate data.  (RH is required to calculate FWI.) Year: {0}, day: {1}, climate region: {2}, NumSites={3}", ActualYear, day, climateRegion.Name, sitesPerClimateRegion[climateRegion.Index]);
                            throw new System.ApplicationException(mesg);
                        }


                    }
                }

             
                
                PlugIn.ModelCore.UI.WriteLine("   Generating accidental fires...");
                // if (shuffledAccidentalFireSites.Count > 0 && landscapeAverageFireWeatherIndex >= 10.0)
                /// Removed FWI threshold ZR 11-12-20
                if (numAccidentalSites > 0)
                {
                    bool fire = false;
                    int maxNumAccidentalFires = NumberOfIgnitions(IgnitionType.Accidental, landscapeAverageFireWeatherIndex);
                    int logMaxNumAccidentalFires = maxNumAccidentalFires;
                    int actualNumAccidentalFires = 0;
                    while (maxNumAccidentalFires > 0)
                    {
                        //Ignite(Ignition.Accidental, shuffledAccidentalFireSites, day, landscapeAverageFireWeatherIndex);
                        fire = Ignite(IgnitionType.Accidental, weightedAccidentalSites.Select(), day, landscapeAverageFireWeatherIndex);
                        if (fire)
                        {
                            maxNumAccidentalFires--;
                            actualNumAccidentalFires++;
                        }
                    }
                    if (fire)
                    {
                        LogIgnition(ModelCore.CurrentTime, landscapeAverageFireWeatherIndex, IgnitionType.Accidental.ToString(), logMaxNumAccidentalFires, actualNumAccidentalFires, day);
                    }
                }

                /// Removed FWI threshold ZR 11-12-20
                PlugIn.ModelCore.UI.WriteLine("   Generating lightning fires...");
                if (numLightningSites > 0)
                {
                    bool fire = false;
                    int maxNumLightningFires = NumberOfIgnitions(IgnitionType.Lightning, landscapeAverageFireWeatherIndex);
                    int logMaxNumLightningFires = maxNumLightningFires;
                    int actualNumLightningFires = 0;
                    while(maxNumLightningFires > 0)
                    {
                        //Ignite(Ignition.Lightning, shuffledLightningFireSites, day, landscapeAverageFireWeatherIndex);
                        fire = Ignite(IgnitionType.Lightning, weightedLightningSites.Select(), day, landscapeAverageFireWeatherIndex);
                        if (fire)
                        {
                            maxNumLightningFires--;
                            actualNumLightningFires++;
                        }
                    }
                    if (fire)
                    {
                        LogIgnition(ModelCore.CurrentTime, landscapeAverageFireWeatherIndex, IgnitionType.Lightning.ToString(), logMaxNumLightningFires, actualNumLightningFires, day);
                    }
                }

                // Ignite a single Rx fire per day
                PlugIn.ModelCore.UI.WriteLine("   Generating prescribed fire...");


                if (numRxSites > 0 &&
                    numAnnualRxFires > 0 &&
                    landscapeAverageFireWeatherIndex > Parameters.RxMinFireWeatherIndex &&
                    landscapeAverageFireWeatherIndex < Parameters.RxMaxFireWeatherIndex &&
                    landscapeAverageTemperature < Parameters.RxMaxTemperature &&
                    landscapeAverageRelHumidity > Parameters.RxMinRelativeHumidity &&
                    weatherData.DailyWindSpeed[day] < Parameters.RxMaxWindSpeed &&
                    day >= Parameters.RxFirstDayFire &&
                    day < Parameters.RxLastDayFire)
                {
                    int maxNumDailyRxFires = Parameters.RxNumberDailyFires;
                    int actualNumRxFires = 0;
                    bool fire = false;

                    while (numAnnualRxFires > 0 && maxNumDailyRxFires > 0)
                    {
                        fire = Ignite(IgnitionType.Rx, weightedRxSites.Select(), day, landscapeAverageFireWeatherIndex);
                        if (fire)
                        {
                            numAnnualRxFires--;
                            maxNumDailyRxFires--;
                            actualNumRxFires++;
                        }
                    }
                    if (fire)
                    {
                        LogIgnition(ModelCore.CurrentTime, landscapeAverageFireWeatherIndex, IgnitionType.Rx.ToString(), Parameters.RxNumberDailyFires, actualNumRxFires, day);
                    }
                }
            }

            modelCore.UI.WriteLine("   Done processing fire days.  Next, write fire maps and summary fire files. ...");

            WriteMaps(PlugIn.ModelCore.CurrentTime);

            WriteSummaryLog(PlugIn.ModelCore.CurrentTime);

            if (isDebugEnabled)
                modelCore.UI.WriteLine("Done running extension");
        }


        //---------------------------------------------------------------------

        private void WriteMaps(int currentTime)
        {
            string[] paths = { "scrapple-fire", "special-dead-wood-{timestep}.img" };
            //string path = MapNames.ReplaceTemplateVars("scrapple-fire/special-dead-wood-{timestep}.img", currentTime);
            string path = MapNames.ReplaceTemplateVars(Path.Combine(paths), currentTime);

            using (IOutputRaster<IntPixel> outputRaster = modelCore.CreateRaster<IntPixel>(path, modelCore.Landscape.Dimensions))
            {
                IntPixel pixel = outputRaster.BufferPixel;
                foreach (Site site in PlugIn.ModelCore.Landscape.AllSites)
                {
                    if (site.IsActive)
                    {
                        if (SiteVars.Disturbed[site] && SiteVars.Intensity[site] > 0)
                        {
                            pixel.MapCode.Value = (int)(SiteVars.SpecialDeadWood[site]);
                        }
                        else
                            pixel.MapCode.Value = 0;
                    }
                    else
                    {
                        //  Inactive site
                        pixel.MapCode.Value = 0;
                    }
                    outputRaster.WriteBufferPixel();
                }
            }
            string[] paths2 = { "scrapple-fire", "ignition-type-{timestep}.img" };
            //path = MapNames.ReplaceTemplateVars("scrapple-fire/ignition-type-{timestep}.img", currentTime);
            path = MapNames.ReplaceTemplateVars(Path.Combine(paths2), currentTime);


            using (IOutputRaster<IntPixel> outputRaster = modelCore.CreateRaster<IntPixel>(path, modelCore.Landscape.Dimensions))
            {
                IntPixel pixel = outputRaster.BufferPixel;
                foreach (Site site in PlugIn.ModelCore.Landscape.AllSites)
                {
                    if (site.IsActive)
                    {
                        if (SiteVars.Disturbed[site] && SiteVars.Intensity[site] > 0)
                        {
                            pixel.MapCode.Value = (SiteVars.TypeOfIginition[site] + 2);
                        }
                        else
                            pixel.MapCode.Value = 1;
                    }
                    else
                    {
                        //  Inactive site
                        pixel.MapCode.Value = 0;
                    }
                    outputRaster.WriteBufferPixel();
                }
            }

            string[] paths3 = { "scrapple-fire", "fire-intensity-{timestep}.img" };
            path = MapNames.ReplaceTemplateVars(Path.Combine(paths3), currentTime);
            using (IOutputRaster<IntPixel> outputRaster = modelCore.CreateRaster<IntPixel>(path, modelCore.Landscape.Dimensions))
            {
                IntPixel pixel = outputRaster.BufferPixel;
                foreach (Site site in PlugIn.ModelCore.Landscape.AllSites)
                {
                    if (site.IsActive)
                    {
                        if (SiteVars.Disturbed[site] && SiteVars.Intensity[site] > 0)
                            pixel.MapCode.Value = (int) (SiteVars.Intensity[site] + 1);
                        else
                            pixel.MapCode.Value = 1;
                    }
                    else
                    {
                        //  Inactive site
                        pixel.MapCode.Value = 0;
                    }
                    outputRaster.WriteBufferPixel();
                }
            }

            string[] paths4 = { "scrapple-fire", "fire-spread-probability-{timestep}.img" };
            path = MapNames.ReplaceTemplateVars(Path.Combine(paths4), currentTime);
            using (IOutputRaster<ShortPixel> outputRaster = modelCore.CreateRaster<ShortPixel>(path, modelCore.Landscape.Dimensions))
            {
                ShortPixel pixel = outputRaster.BufferPixel;
                foreach (Site site in PlugIn.ModelCore.Landscape.AllSites)
                {
                    if (site.IsActive)
                    {
                        if (SiteVars.Disturbed[site] && SiteVars.Intensity[site] > 0)
                            pixel.MapCode.Value = (short)(SiteVars.SpreadProbability[site] * 100);
                        else
                            pixel.MapCode.Value = 0;
                    }
                    else
                    {
                        //  Inactive site
                        pixel.MapCode.Value = 0;
                    }
                    outputRaster.WriteBufferPixel();
                }
            }

            string[] paths5 = { "scrapple-fire", "day-of-fire-{timestep}.img" };
            path = MapNames.ReplaceTemplateVars(Path.Combine(paths5), currentTime);
            using (IOutputRaster<ShortPixel> outputRaster = modelCore.CreateRaster<ShortPixel>(path, modelCore.Landscape.Dimensions))
            {
                ShortPixel pixel = outputRaster.BufferPixel;
                foreach (Site site in PlugIn.ModelCore.Landscape.AllSites)
                {
                    if (site.IsActive)
                    {
                        if (SiteVars.Disturbed[site] && SiteVars.Intensity[site] > 0)
                            pixel.MapCode.Value = (short) (SiteVars.DayOfFire[site] + 1);
                        else
                            pixel.MapCode.Value = 999;  // distinguish from January 1
                    }
                    else
                    {
                        //  Inactive site
                        pixel.MapCode.Value = 0;
                    }
                    outputRaster.WriteBufferPixel();
                }
            }

            string[] paths6 = { "scrapple-fire", "smolder-consumption-{timestep}.img" };
            path = MapNames.ReplaceTemplateVars(Path.Combine(paths6), currentTime);
            using (IOutputRaster<IntPixel> outputRaster = modelCore.CreateRaster<IntPixel>(path, modelCore.Landscape.Dimensions))
            {
                IntPixel pixel = outputRaster.BufferPixel;
                foreach (Site site in PlugIn.ModelCore.Landscape.AllSites)
                {
                    if (site.IsActive)
                    {
                        if (SiteVars.Disturbed[site] && SiteVars.Intensity[site] > 0)
                            pixel.MapCode.Value = SiteVars.SmolderConsumption[site];
                        else
                            pixel.MapCode.Value = 0;
                    }
                    else
                    {
                        //  Inactive site
                        pixel.MapCode.Value = 0;
                    }
                    outputRaster.WriteBufferPixel();
                }
            }

            string[] paths7 = { "scrapple-fire", "flaming-consumptions-{timestep}.img" };
            path = MapNames.ReplaceTemplateVars(Path.Combine(paths7), currentTime);
            using (IOutputRaster<IntPixel> outputRaster = modelCore.CreateRaster<IntPixel>(path, modelCore.Landscape.Dimensions))
            {
                IntPixel pixel = outputRaster.BufferPixel;
                foreach (Site site in PlugIn.ModelCore.Landscape.AllSites)
                {
                    if (site.IsActive)
                    {
                        if (SiteVars.Disturbed[site] && SiteVars.Intensity[site] > 0)
                            pixel.MapCode.Value = SiteVars.FlamingConsumption[site];
                        else
                            pixel.MapCode.Value = 0;
                    }
                    else
                    {
                        //  Inactive site
                        pixel.MapCode.Value = 0;
                    }
                    outputRaster.WriteBufferPixel();
                }
            }

            string[] paths8 = { "scrapple-fire", "event-ID-{timestep}.img" };
            path = MapNames.ReplaceTemplateVars(Path.Combine(paths8), currentTime);
            using (IOutputRaster<IntPixel> outputRaster = modelCore.CreateRaster<IntPixel>(path, modelCore.Landscape.Dimensions))
            {
                IntPixel pixel = outputRaster.BufferPixel;
                foreach (Site site in PlugIn.ModelCore.Landscape.AllSites)
                {
                    if (site.IsActive)
                    {
                        if (SiteVars.Disturbed[site] && SiteVars.Intensity[site] > 0)
                            pixel.MapCode.Value = SiteVars.EventID[site];
                        else
                            pixel.MapCode.Value = 0;
                    }
                    else
                    {
                        //  Inactive site
                        pixel.MapCode.Value = 0;
                    }
                    outputRaster.WriteBufferPixel();
                }
            }
            string[] paths9 = { "scrapple-fire", "fine-fuels-{timestep}.img" };
            path = MapNames.ReplaceTemplateVars(Path.Combine(paths9), currentTime);
            using (IOutputRaster<IntPixel> outputRaster = modelCore.CreateRaster<IntPixel>(path, modelCore.Landscape.Dimensions))
            {
                IntPixel pixel = outputRaster.BufferPixel;
                foreach (Site site in PlugIn.ModelCore.Landscape.AllSites)
                {
                    if (site.IsActive)
                    {
                        pixel.MapCode.Value = (int) SiteVars.FineFuels[site];
                    }
                    else
                    {
                        //  Inactive site
                        pixel.MapCode.Value = 0;
                    }
                    outputRaster.WriteBufferPixel();
                }
            }
        }


        //---------------------------------------------------------------------

        //  Determines the number of Ignitions per day
        //  Returns: 0 <= numIgnitons <= 3
        private static int NumberOfIgnitions(IgnitionType ignitionType, double fireWeatherIndex)
        {
            double b0 = 0.0;
            double b1 = 0.0;
            if(ignitionType == IgnitionType.Lightning)
            {
                b0 = Parameters.LightningIgnitionB0;
                b1 = Parameters.LightningIgnitionB1;
            }
            if (ignitionType == IgnitionType.Accidental)
            {
                b0 = Parameters.AccidentalFireIgnitionB0;
                b1 = Parameters.AccidentalFireIgnitionB1;
            }

            int numIgnitions = 0;
            if (IgnitionDist == IgnitionDistribution.Poisson)
            {
                //Draw from a poisson distribution  with lambda equal to the log link (b0 +b0 *fireweather )
                double possibleIgnitions = ModelCore.PoissonDistribution.Lambda = Math.Pow(Math.E, (b0 + (b1 * fireWeatherIndex)));
               // Because the Core Poisson Distribution Lambda returns the population mean we transform it to the whole number + the probability of the remainder to get 
               // a integer as the response. 
               // Whole Number
                int floorPossibleIginitions = (int)Math.Floor(possibleIgnitions);
                numIgnitions += floorPossibleIginitions;
                // Remainder 
                numIgnitions += (modelCore.GenerateUniform() <= (possibleIgnitions - (double)floorPossibleIginitions) ? 1 : 0);
                //modelCore.UI.WriteLine("   Processing landscape for Fire events.  Possible={0}, Rounded={1}", possibleIgnitions, numIgnitions);
            } else
            {
                // Zero Inflated: Requires two additional variables 
                double binomb0 = 0.0;
                double binomb1 = 0.0;
                
                if (ignitionType == IgnitionType.Lightning)
                {
                    binomb0 = Parameters.LightningIgnitionBinomialB0;
                    binomb1 = Parameters.LightningIgnitionBinomialB1;
                }
                if (ignitionType == IgnitionType.Accidental)
                {
                    binomb0 = Parameters.AccidentalFireIgnitionBinomialB0;
                    binomb1 = Parameters.AccidentalFireIgnitionBinomialB1;
                }
                /// The Binomial portion of the draw: 
                /// Probability of a zero is caculated then a random draw is checked agianst this 
                /// If greater than the probability of zero the Poisson section is used. 
                double BinomDraw = modelCore.NextDouble();
                /// alpha= reverse logit link of the regression values and FWI
                double alpha = Math.Pow(Math.E, (binomb0 + (binomb1 * fireWeatherIndex)));
                double zerosprob = alpha / (alpha + 1);
                if (BinomDraw >= zerosprob)
                {
                    /// If yes the mean of possion draw with reverse log link of regression variables. 
                    double  possibleIgnitions=ModelCore.PoissonDistribution.Lambda = Math.Pow(Math.E, (b0 + (b1 * fireWeatherIndex)));
                    ///Because the the Core Poisson Distribution Lambda returns the population mean we transform it to the whole number + the probability of the remainder to get 
                    /// a integer as the response. 
                    /// 
                    /// Whole Number
                    int floorPossibleIginitions = (int)Math.Floor(possibleIgnitions);
                    numIgnitions += floorPossibleIginitions;
                    /// Remainder 
                    numIgnitions += (modelCore.GenerateUniform() <= (possibleIgnitions - (double)floorPossibleIginitions) ? 1 : 0);
                }
                else
                {
                    numIgnitions = 0;
                }
            }
            return numIgnitions;
        }

        //---------------------------------------------------------------------
        // Ignites and Spreads a fire
        private static bool Ignite(IgnitionType ignitionType, ActiveSite site, int day, double fireWeatherIndex)
        {
            if (SiteVars.Disturbed[site])
                return false;
            FireEvent fireEvent = FireEvent.Initiate(site, modelCore.CurrentTime, day, ignitionType);

            totalBurnedSites[(int)ignitionType] += fireEvent.TotalSitesBurned;
            numberOfFire[(int)ignitionType]++;
            totalBiomassMortality[(int)ignitionType] += (int)fireEvent.TotalBiomassMortality;
            dNBR[(int)ignitionType] += (int)fireEvent.SiteMortality;
            numCellsSeverity1 += fireEvent.NumberCellsSeverity1;
            numCellsSeverity2 += fireEvent.NumberCellsSeverity2;
            numCellsSeverity3 += fireEvent.NumberCellsSeverity3;
            numCellsSeverity4 += fireEvent.NumberCellsSeverity4;
            numCellsSeverity5 += fireEvent.NumberCellsSeverity5;
            return true;
        }

        // This deprecated method uses a brute force sort that is much slower.
        //private static void Ignite(IgnitionType ignitionType, List<ActiveSite> shuffledFireSites, int day, double fireWeatherIndex)
        //{
        //    while (shuffledFireSites.Count() > 0 && SiteVars.Disturbed[shuffledFireSites.First()] == true)
        //    {
        //        shuffledFireSites.Remove(shuffledFireSites.First());
        //    }
        //    if (shuffledFireSites.Count() > 0)
        //    {
        //        FireEvent fireEvent = FireEvent.Initiate(shuffledFireSites.First(), modelCore.CurrentTime, day, ignitionType);

        //        totalBurnedSites[(int) ignitionType] += fireEvent.TotalSitesBurned;
        //        numberOfFire[(int)ignitionType]++;
        //        totalBiomassMortality[(int)ignitionType] += (int)fireEvent.TotalBiomassMortality;
        //        numCellsSeverity1 += fireEvent.NumberCellsSeverity1;
        //        numCellsSeverity2 += fireEvent.NumberCellsSeverity2;
        //        numCellsSeverity3 += fireEvent.NumberCellsSeverity3;
        //    }
        //}

        //---------------------------------------------------------------------

        // Determines if an Rx Fire will start on the given day
        // RMS TODO
        private bool AllowRxFire(int day, double fireWeatherIndex)
        {
            return false;
        }

        //---------------------------------------------------------------------

        public static void LogIgnition(int currentTime, double fwi, string type, int attemptedNumIgns, int actualNumIgns, int doy)
        {

            ignitionsLog.Clear();
            IgnitionsLog ign = new IgnitionsLog();
            ign.SimulationYear = currentTime;
            ign.AttemptedNumberIgnitions = attemptedNumIgns;
            ign.ActualNumberIgnitions = actualNumIgns;
            ign.DayOfYear = doy;
            ign.FireWeatherIndex = fwi;
            ign.IgnitionType = type;

            ignitionsLog.AddObject(ign);
            ignitionsLog.WriteToFile();

        }



        //---------------------------------------------------------------------
        // A helper function to shuffle a list of ActiveSties: Algorithm may be improved.
        // Sites are weighted for ignition in the Shuffle method, based on the respective inputs maps.
        // This function uses a fast open-source sort routine: 8/2019
        private static WeightedSelector<ActiveSite> PreShuffleEther(ISiteVar<double> weightedSiteVar, out int numSites)
        {
            WeightedSelector<ActiveSite> wselector = new WeightedSelector<ActiveSite>();
            //List<ActiveSite> list = new List<ActiveSite>();
            numSites = 0;
            foreach (ActiveSite site in PlugIn.ModelCore.Landscape.ActiveSites)
                if (weightedSiteVar[site] > 0.0)
                {
                    wselector.Add(site, ((int)weightedSiteVar[site]));
                    numSites++;
                }

            return wselector;

        }

        //---------------------------------------------------------------------
        // A helper function to shuffle a list of ActiveSties: Algorithm may be improved.
        // Sites are weighted for ignition in the Shuffle method, based on the respective inputs maps.
        private static List<ActiveSite> PreShuffle(ISiteVar<double> weightedSiteVar, out double totalWeight)
        {
            //WeightedSelector<ActiveSite> wselector = new WeightedSelector<ActiveSite>();
            List<ActiveSite> list = new List<ActiveSite>(); 
            foreach (ActiveSite site in PlugIn.ModelCore.Landscape.ActiveSites)
                if (weightedSiteVar[site] > 0.0)
                    list.Add(site);

            //List<ActiveSite> shuffledList = new List<ActiveSite>();
            totalWeight = 0.0;
            foreach (ActiveSite site in list)
            {
                totalWeight += weightedSiteVar[site];
            }

            return list;
        }


        private static List<ActiveSite> Shuffle(List<ActiveSite> list, ISiteVar<double> weightedSiteVar, double totalWeight)
        {
            //List<ActiveSite> list = new List<ActiveSite>(); ;
            //foreach (ActiveSite site in PlugIn.ModelCore.Landscape.ActiveSites)
            //    if (weightedSiteVar[site] > 0.0)
            //        list.Add(site);

            //double totalWeight = 0;
            //foreach (ActiveSite site in list)
            //{
            //    totalWeight += weightedSiteVar[site];
            //}

            List<ActiveSite> shuffledList = new List<ActiveSite>();
            while (list.Count > 0)
            {
                ActiveSite toAdd;
                toAdd = SelectRandomSite(list, weightedSiteVar, totalWeight);
                shuffledList.Add(toAdd);
                totalWeight -= weightedSiteVar[toAdd];
                list.Remove(toAdd);
            }

            return shuffledList;
        }

        //---------------------------------------------------------------------
        // The random selection based on input map weights
        public static ActiveSite SelectRandomSite(List<ActiveSite> list, ISiteVar<double> weightedSiteVar, double totalWeight)
        {
            ActiveSite selectedSite = list.FirstOrDefault(); // currently selected element

            int randomNum = FireEvent.rnd.Next((int)totalWeight);
            while (randomNum > totalWeight)
            {
                randomNum = FireEvent.rnd.Next(list.Count);
            }

            //check to make sure it is 
            foreach (ActiveSite site in list)
            {
                if (randomNum < weightedSiteVar[site])
                {
                    selectedSite = site;
                    break;
                }

                randomNum -= (int)weightedSiteVar[site];
            }

            return selectedSite; // when iterations end, selected is some element of sequence. 
        }
        //---------------------------------------------------------------------

        private void WriteSummaryLog(int currentTime)
        {
            summaryLog.Clear();
            SummaryLog sl = new SummaryLog();
            sl.SimulationYear = currentTime;
            sl.TotalBurnedSitesAccidental = totalBurnedSites[0];
            sl.TotalBurnedSitesLightning = totalBurnedSites[1];
            sl.TotalBurnedSitesRx = totalBurnedSites[2];
            sl.NumberFiresAccidental = numberOfFire[0];
            sl.NumberFiresLightning = numberOfFire[1];
            sl.NumberFiresRx = numberOfFire[2];
            sl.TotalBiomassMortalityAccidental = totalBiomassMortality[0];
            sl.TotalBiomassMortalityLightning = totalBiomassMortality[1];
            sl.TotalBiomassMortalityRx = totalBiomassMortality[2];
            sl.NumberCellsSeverity1 = numCellsSeverity1;
            sl.NumberCellsSeverity2 = numCellsSeverity2;
            sl.NumberCellsSeverity3 = numCellsSeverity3;
            sl.NumberCellsSeverity4 = numCellsSeverity4;
            sl.NumberCellsSeverity5 = numCellsSeverity5;


            summaryLog.AddObject(sl);
            summaryLog.WriteToFile();
        }

        /*
         * VS:: THIS MAY BE NEEDED
         * 
        private static List<ActiveSite> Sort<T>(List<ActiveSite> list, ISiteVar<T> type)
        {
            List<ActiveSite> sortedList = new List<ActiveSite>();
            foreach(ActiveSite site in list)
            {
                type[site] 
            }
        }
        */

    }
}
