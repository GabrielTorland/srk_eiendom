﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using srk_website.Data;
using srk_website.Models;
using srk_website.Services;

namespace srk_website.Controllers
{
    [Authorize]
    public class TeamController : Controller
    {
        private readonly IAzureStorage _storage;
        private readonly List<string> _imageFormats;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ImageSlideShowController> _logger;
        private readonly IGenerateRandomImageName _generator;

        public TeamController(IAzureStorage storage, ApplicationDbContext context, IConfiguration configuration, ILogger<ImageSlideShowController> logger, IGenerateRandomImageName generator)
        {
            _storage = storage;
            _context = context;
            // List of image formats supported, see appsettings.json.
            _imageFormats = configuration.GetSection("Formats:Images").Get<List<string>>();
            _logger = logger;
            _generator = generator;
        }

        // GET: Team
        public async Task<IActionResult> Index()
        {
              return _context.Team != null ? 
                          View(await _context.Team.ToListAsync()) :
                          Problem("Entity set 'ApplicationDbContext.Team'  is null.");
        }

        // GET: Team/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null || _context.Team == null)
            {
                return NotFound();
            }

            var teamModel = await _context.Team
                .FirstOrDefaultAsync(m => m.Id == id);
            if (teamModel == null)
            {
                return NotFound();
            }

            return View(teamModel);
        }

        // GET: Team/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Team/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [System.ComponentModel.Description("Upload image to azure container and store meta data in database.")]
        public async Task<IActionResult> Create([Bind(include: "FirstName,LastName,Position,Email,Phone,LinkedIn")] TeamModel team, IFormFile file)
        {
            if (file == null)
            {
                ViewBag.IsResponse = true;
                ViewBag.IsSuccess = false;
                ViewBag.Message = "All parameters needs to be filled!";
                return View();
            }
            
            if (ModelState.IsValid)
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
                    if (_context.Team.Where(s => s.ImageName == fileName).Count() == 0)
                    {
                        break;
                    }
                }

                // Upload image to azure container.
                BlobResponseDto? response = await _storage.UploadAsync(file, fileName);

                // Check if we got an error
                if (response.Error == true)
                {
                    // We got an error during upload, return an error with details to the client
                    _logger.LogError("Failed to upload to azure container.");
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
                    team.ImageName = fileName;
                    team.Uri = uri;
                    // Try catch here in the future.
                    await _context.Team.AddAsync(team);
                    await _context.SaveChangesAsync();
                    ViewBag.IsResponse = true;
                    ViewBag.IsSuccess = true;
                    ViewBag.Message = "Image was successfully uploaded to the slideshow!";
                    
                    return View();
                }
            }
            return View(team);
        }

        // GET: Team/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null || _context.Team == null)
            {
                return NotFound();
            }

            var teamModel = await _context.Team.FindAsync(id);
            if (teamModel == null)
            {
                return NotFound();
            }
            return View(teamModel);
        }

        // POST: Team/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [System.ComponentModel.Description("Edit image in azure container and edit meta data in database.")]
        public async Task<IActionResult> Edit(int id, [Bind(include: "Id, FirstName,LastName,Position,Email,Phone,LinkedIn,ImageName,Uri")] TeamModel team, IFormFile file)
        {
            if (id != team.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                if (file != null) {
                    
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
                    
                    // Delete old image from azure container.
                    BlobResponseDto response = await _storage.DeleteAsync(team.ImageName);
                    if (response.Error == true)
                    {
                        _logger.LogError("Failed to delete image from azure container.");
                        return StatusCode(StatusCodes.Status500InternalServerError, response.Status);
                    }
                    else
                    {
                        // Upload new image to azure container.
                        string imageName = team.ImageName.Split('.')[0] + '.' + ContentType[1];
                        response = await _storage.UploadAsync(file, imageName);
                        if (response.Error == true)
                        {
                            _logger.LogError("Failed to new upload image to azure container.");
                            return StatusCode(StatusCodes.Status500InternalServerError, response.Status);
                        }
                        else
                        {
                            // Update the team member
                            team.Uri = team.Uri.Replace(team.ImageName, imageName);
                            team.ImageName = imageName;
                        }
                    }
                }
                try
                {
                    _context.Update(team);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!TeamModelExists(team.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            return View(team);
        }

        // POST: Team/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [System.ComponentModel.Description("Delete image in azure container and meta data in database.")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (_context.Team == null)
            {
                return Problem("Entity set 'ApplicationDbContext.Team'  is null.");
            }
            var team = await _context.Team.FindAsync(id);
            if (team == null)
            {
                return NotFound();
            }
            string imageName = team.ImageName;
            
            // Delete meta data from database
            _context.Team.Remove(team);
            await _context.SaveChangesAsync();
            
            // Delete image from azure container.
            BlobResponseDto response = await _storage.DeleteAsync(imageName);
            
            // Check if we got an error
            if (response.Error == true)
            {
                // Return an error message to the client
                return StatusCode(StatusCodes.Status500InternalServerError, response.Status);
            }
            else
            {
                // File has been successfully deleted
                return RedirectToAction("Index", "Team");
            }
        }

        private bool TeamModelExists(int id)
        {
          return (_context.Team?.Any(e => e.Id == id)).GetValueOrDefault();
        }
    }
}