﻿using SatelliteWallpaperUpdater.Interfaces.Repositories;
using SatelliteWallpaperUpdater.Interfaces.Repositories.Mappers;
using SatelliteWallpaperUpdater.Models;
using System.Text.RegularExpressions;

namespace SatelliteWallpaperUpdater.Repositories
{
    public class NESDISRepository : INESDISRepository
    {
        private readonly HttpClient _httpClient;
        private readonly IGOES16GeoColorMetadataMapper _goes16MetadataMapper;

        //TODO make this config driven so if things change on the satellite it can be easily replaced
        private const string BaseFileName = "_GOES16-ABI-FD-GEOCOLOR-";
        private readonly Uri BaseURI = new Uri("https://cdn.star.nesdis.noaa.gov/GOES16/ABI/FD/GEOCOLOR/");

        public NESDISRepository(IGOES16GeoColorMetadataMapper goes16MetadataMapper)
        {
            _httpClient = new HttpClient();
            _goes16MetadataMapper = goes16MetadataMapper;
        }

        public async Task<string> GetAvailableImagesAsync()
        {
            var response = await _httpClient.GetAsync(BaseURI);
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<SatelliteImage> GetImageAsync(SatelliteImageMetadata metadata, string pathToSaveFile)
        {
            string path = $"{pathToSaveFile}\\{metadata.FullFileName}";

            using (Stream request = await _httpClient.GetStreamAsync(BaseURI + metadata.FullFileName))
            using (FileStream file = File.OpenWrite(path))
            {
                request.CopyTo(file);
            }

            return new SatelliteImage(metadata, path);
        }

        /// <summary>
        /// Gets the latest image 
        /// </summary>
        /// <returns>the resulting metadata of the latest file</returns>

        public async Task<List<SatelliteImageMetadata>> GetLatestImagesMetadataAsync()
        {
            DateTime currentDate = DateTime.UtcNow;
            List<SatelliteImageMetadata> imageMetadatas = new List<SatelliteImageMetadata>();

            // get the list of files then separate it into lines for parsing 
            string availableFiles = await _httpClient.GetStringAsync(BaseURI);
            string[] listOfFiles = availableFiles.Split('\n');

            /* This will look at the resulting html and match on >20243302100_GOES16-ABI-FD-GEOCOLOR-10848x10848.jpg<
             * This is using the closing bracket in the html tag as our match start, and the end is signfied by the open bracket >
             * Example: 
             * >20243302100_GOES16-ABI-FD-GEOCOLOR-10848x10848.jpg< 
             * is a match from:
             * <a href="20243302100_GOES16-ABI-FD-GEOCOLOR-10848x10848.jpg">20243302100_GOES16-ABI-FD-GEOCOLOR-10848x10848.jpg</a> 25-Nov-2024 21:15       
            */
            Regex regex = new Regex(">([0-9]{11})(.+).jpg<");

            // Kepp the max date to return a subset later
            DateTime maxDate = DateTime.MinValue;

            for (int i = listOfFiles.Length - 1; i >= 0; i--)
            {
                var test = listOfFiles[i];
                var match = regex.Match(listOfFiles[i]);

                if (match.Success)
                {
                    // map to satelliteimage here then we can sort by datetime and return.
                    string actualFileName = match.Value.Substring(1, match.Value.Length - 2);
                    imageMetadatas.Add(_goes16MetadataMapper.MapViaFileName(actualFileName));

                    // if we have a new max date, update it to know which set of images to return.
                    if (maxDate < imageMetadatas[imageMetadatas.Count - 1].ObservationDate)
                    {
                        maxDate = imageMetadatas[imageMetadatas.Count - 1].ObservationDate;
                    }
                }
            }

            return imageMetadatas.Where(metadata => metadata.ObservationDate == maxDate).ToList();
        }

        private string FileNameFromDesiredDate(DateTime date)
        {
            /// see http://cimss.ssec.wisc.edu/goes/ABI_File_Naming_Conventions.pdf for more details on this. 
            /// The documentation isn't great for this specific branch of NOAA. Hence why we can't use an api like we could for weather
            string DatePortion = $"{date.Year}{date.DayOfYear}{date.Hour}{date.Minute:D2}";

            return DatePortion + BaseFileName + "5424x5424" + ".jpg";
        }
    }
}