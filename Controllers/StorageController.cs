﻿using srk_website.Models;
using srk_website.Services;
using Microsoft.AspNetCore.Mvc;
using srk_website.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace srk_website.Controllers
{
    [Route("api/[controller]")]
    [Authorize]
    public class StorageController : Controller
    {
        private readonly IAzureStorage _storage;
        private readonly List<string> _imageFormats;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ImageSlideShowController> _logger;
        private readonly IGenerateRandomImageName _generator;

        public StorageController(IAzureStorage storage, ApplicationDbContext context, IConfiguration configuration, ILogger<ImageSlideShowController> logger, IGenerateRandomImageName generator)
        {
            _storage = storage;
            _context = context;
            // List of image formats supported, see appsettings.json.
            _imageFormats = configuration.GetSection("Formats:Images").Get<List<string>>();
            _logger = logger;
            _generator = generator;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            // Returns an empty array if no files are present at the storage container
            return _context.Contact != null ?
                          View(await _context.Storage.ToListAsync()) :
                          Problem("Entity set 'ApplicationDbContext.Storage'  is null.");
        }

        [HttpGet(nameof(Upload))]
        public IActionResult Upload()
        {
            return View();
        }

        [HttpPost(nameof(Upload))]
        [System.ComponentModel.Description("Upload image to azure container and store meta data in database.")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            // Index 0 is description of the data, e.g image.
            // Index 1 is the datatype, e.g jpg... 
            var ContentType = file.ContentType.Split("/");
            if (ContentType[0] != "image")
            {
                ViewBag.IsResponse = true;
                ViewBag.IsSuccess = false;
                ViewBag.Message = "You can only upload an image!";
                return View();
            }
            if (!_imageFormats.Contains(ContentType[1]))
            {
                ViewBag.IsResponse = true;
                ViewBag.IsSuccess = false;
                var formats = _imageFormats.ToString();
                ViewBag.Message = $"Formats supported: {formats}";
                return View();
            }

            // Generating fileNames untill a unique is found.
            string fileName = "";
            while (true)
            {
                fileName = await _generator.Generate(ContentType[1], 20);
                if (_context.Storage.Where(s => s.ImageName == fileName).Count() == 0)
                {
                    break;
                }
            }
            
            BlobResponseDto? response = await _storage.UploadAsync(file, fileName);

            // Check if we got an error
            if (response.Error == true)
            {
                // We got an error during upload, return an error with details to the client
                return StatusCode(StatusCodes.Status500InternalServerError, response.Status);
            }
            else
            {
                var images = await _storage.ListAsync();
                string uri = "";
                foreach (var image in images)
                {
                    if (image.Name == fileName)
                    {
                        uri = image.Uri;
                    }
                }
                if (uri == "")
                {
                    return Problem("Could not find image in azure container!");
                }
                // Meta data about image.
                StorageModel newImage = new StorageModel(fileName, uri);

                // Stored in the database.
                // Try catch here in the future.
                await _context.Storage.AddAsync(newImage);
                await _context.SaveChangesAsync();

                ViewBag.IsResponse = true;
                ViewBag.IsSuccess = true;
                ViewBag.Message = "Image was successfully uploaded!";
                return View();
            }
        }

        [HttpPost(nameof(Delete))]
        [ValidateAntiForgeryToken]
        [System.ComponentModel.Description("Delete image in azure container and meta data in database.")]
        public async Task<IActionResult> Delete(string ImageName)
        {
            if (ImageName == null)
            {
                return Problem("ImageName cant be null.");
            }

            // Find image in database.
            var image = await _context.Storage.FirstOrDefaultAsync(s => s.ImageName == ImageName);
            if (image == null)
            {
                _logger.LogError("Image meta data is not in database.");
                // Return an error message to the client
                return StatusCode(StatusCodes.Status500InternalServerError, "Image meta data is not in database.");
            }
            // Remove image from database.
            // Try catch here in the future.
            _context.Storage.Remove(image);
            await _context.SaveChangesAsync();

            // Delete image from azure container.
            BlobResponseDto response = await _storage.DeleteAsync(ImageName);

            // Check if we got an error
            if (response.Error == true)
            {
                // Return an error message to the client
                return StatusCode(StatusCodes.Status500InternalServerError, response.Status);
            }
            else
            {
                // File has been successfully deleted
                return RedirectToAction("Index", "Storage");
            }
        }
    }
}