using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Mocha2023.Controllers
{

    [ApiController]
    [Mocha2023.Classes.ApiProtection]
    public sealed class LegacyCompatibilityController : ControllerBase
    {
        private static readonly IReadOnlyList<string> IcebreakerWords = new[]
        {
            "Favorite game",
            "Dream vacation",
            "A hidden talent",
            "Favorite food",
            "Best movie",
            "Favorite song",
            "A funny memory",
            "Dream job",
            "Favorite season",
            "A perfect weekend",
            "Favorite animal",
            "A place you want to visit",
            "Something you collect",
            "Favorite school subject",
            "A skill you want to learn",
            "Favorite holiday",
            "Your ideal superpower",
            "A game everyone should try",
            "Something that makes you laugh",
            "Your favorite room in Mocha",
            "Best thing that happened this week",
            "A food you could eat forever",
            "Something you are proud of",
            "Your funniest autocorrect",
            "Favorite thing to build",
            "Your dream pet",
            "A song stuck in your head",
            "Best birthday memory",
            "A place that feels like home",
            "Your most used emoji",
            "A hobby you want to try",
            "Favorite time of day",
            "A fictional world you would visit",
            "Someone who inspires you",
            "Your comfort movie",
            "A weird food combination you like",
            "Your dream room in Mocha",
            "Your favorite Mocha activity",
            "First game you remember playing",
            "Best advice you ever heard",
            "A harmless unpopular opinion",
            "Something on your bucket list",
            "Favorite smell",
            "Your go-to snack",
            "A skill you are proud of",
            "Dream concert",
            "Favorite weather",
            "A small thing that improves your day",
            "Funniest thing a pet has done",
            "A character you relate to",
            "Your dream teammate",
            "Something you cannot live without",
            "The best gift you have received",
            "Your favorite way to relax",
            "A talent you wish you had",
            "The last thing that made you smile",
            "Your perfect breakfast",
            "A room theme you would create in Mocha",
            "Your favorite outfit",
            "A childhood obsession",
            "The coolest place you have been",
            "A game you are surprisingly good at",
            "Your favorite sound",
            "A challenge you want to beat",
            "Your dream house feature",
            "A superpower you would never choose",
            "Your funniest nickname",
            "One thing you would invent",
            "Your favorite tradition",
            "A movie you can quote forever",
            "Your perfect day off",
            "Something that always cheers you up",
            "Your favorite kind of art",
            "A goal for this year",
            "Your favorite dessert",
            "A topic you could discuss for hours",
            "Your dream vehicle",
            "A trend you actually enjoy",
            "Your favorite thing about Mocha",
            "The first thing you would do on the moon"
        };

        private static readonly IReadOnlyList<string> EasyWords = new[]
        {
            "Apple",
            "Backpack",
            "Basketball",
            "Bicycle",
            "Book",
            "Cat",
            "Chair",
            "Clock",
            "Dog",
            "Guitar",
            "Hat",
            "Ice cream",
            "Phone",
            "Robot",
            "Sandwich",
            "Skateboard",
            "Sun",
            "Tree",
            "Umbrella",
            "Video game",
            "Airplane",
            "Alarm clock",
            "Baby",
            "Banana",
            "Baseball",
            "Bird",
            "Boat",
            "Camera",
            "Candle",
            "Car",
            "Cowboy",
            "Crown",
            "Dinosaur",
            "Drum",
            "Duck",
            "Elephant",
            "Fishing",
            "Flower",
            "Frog",
            "Hammer",
            "Horse",
            "Jump rope",
            "Key",
            "Kite",
            "Lion",
            "Monkey",
            "Moon",
            "Motorcycle",
            "Painting",
            "Penguin",
            "Pizza",
            "Popcorn",
            "Present",
            "Rabbit",
            "Rain",
            "Scissors",
            "Shark",
            "Shoe",
            "Snowman",
            "Soccer",
            "Spoon",
            "Superhero",
            "Swimming",
            "Toothbrush",
            "Train",
            "Turtle",
            "Vacuum",
            "Violin",
            "Washing dishes",
            "Watering a plant",
            "Waving goodbye",
            "Taking a selfie",
            "Sleeping",
            "Sneezing",
            "Laughing",
            "Crying",
            "Dancing",
            "Running",
            "Reading",
            "Cooking"
        };

        private static readonly IReadOnlyList<string> HardWords = new[]
        {
            "Astronaut",
            "Binoculars",
            "Chameleon",
            "Detective",
            "Earthquake",
            "Firefighter",
            "Helicopter",
            "Jellyfish",
            "Knight",
            "Lighthouse",
            "Microscope",
            "Orchestra",
            "Parachute",
            "Roller coaster",
            "Satellite",
            "Submarine",
            "Telescope",
            "Tornado",
            "Volcano",
            "Waterfall",
            "Acrobat",
            "Archaeologist",
            "Avalanche",
            "Blacksmith",
            "Boomerang",
            "Bungee jumping",
            "Castle drawbridge",
            "Circus ringmaster",
            "Construction crane",
            "Deep sea diver",
            "Dragon tamer",
            "Electric guitar solo",
            "Escalator",
            "Film director",
            "Fortune teller",
            "Hot air balloon",
            "Ice sculptor",
            "Juggling chainsaws",
            "Kayaking",
            "Magician",
            "Marionette",
            "Meteor shower",
            "Mountain climber",
            "News reporter",
            "Octopus",
            "Operating a robot",
            "Pirate captain",
            "Police chase",
            "Quicksand",
            "Race car driver",
            "Rock climbing",
            "Scuba diving",
            "Secret agent",
            "Space station",
            "Sword fighting",
            "Tightrope walker",
            "Time traveler",
            "Treasure hunter",
            "UFO landing",
            "Video game streamer",
            "Weather forecast",
            "Werewolf",
            "Wind turbine",
            "Yoga instructor",
            "Zip lining",
            "Airport security",
            "Babysitting twins",
            "Baking a wedding cake",
            "Catching a runaway chicken",
            "Changing a flat tire",
            "Escaping a haunted house",
            "Finding buried treasure",
            "Flying through turbulence",
            "Getting stuck in an elevator",
            "Losing your luggage",
            "Ordering at a drive-through",
            "Riding a mechanical bull",
            "Sneaking past a sleeping dragon",
            "Training a stubborn dog",
            "Walking on the moon"
        };

        private static readonly IReadOnlyList<string> StupidHardWords = new[]
        {
            "Antidisestablishmentarianism",
            "Bioluminescence",
            "Constellation",
            "Cryptography",
            "Electromagnetism",
            "Hibernation",
            "Metamorphosis",
            "Photosynthesis",
            "Procrastination",
            "Quantum mechanics",
            "Revolutionary",
            "Synchronization",
            "Thermodynamics",
            "Unpredictable",
            "Ventriloquist",
            "Aerodynamics",
            "Archaeological excavation",
            "Artificial intelligence",
            "Bureaucracy",
            "Cartography",
            "Circumnavigation",
            "Cloud computing",
            "Cognitive dissonance",
            "Continental drift",
            "Cryptocurrency",
            "Deja vu",
            "Democracy",
            "Ecosystem",
            "Existential crisis",
            "Globalization",
            "Gravity",
            "Identity theft",
            "Imagination",
            "Industrial revolution",
            "Internet connection",
            "Jury duty",
            "Language barrier",
            "Magnetic field",
            "Midlife crisis",
            "Miscommunication",
            "Multitasking",
            "Natural selection",
            "Optical illusion",
            "Parallel universe",
            "Peer pressure",
            "Plate tectonics",
            "Public transportation",
            "Renewable energy",
            "Reverse psychology",
            "Social media algorithm",
            "Solar eclipse",
            "Stock market",
            "Supply and demand",
            "The butterfly effect",
            "Time zone",
            "Traffic congestion",
            "Virtual reality",
            "Writer's block",
            "Zero gravity",
            "Artificial consciousness"
        };

        [HttpGet("/api/activities/charades/v1/words/{category}")]
        public IActionResult GetCharadesWords([FromRoute] string category)
        {
            string normalized = NormalizeCategory(category);

            int difficulty;
            IReadOnlyList<string> words;

            switch (normalized)
            {
                case "icebreakers":
                    difficulty = 20;
                    words = IcebreakerWords;
                    break;

                case "charadeshard":
                case "hard":
                    difficulty = 1;
                    words = HardWords;
                    break;

                case "charadesstupidhard":
                case "stupidhard":
                case "expert":
                    difficulty = 10;
                    words = StupidHardWords;
                    break;

                case "charades":
                case "charadeseasy":
                case "easy":
                default:
                    difficulty = 0;
                    words = EasyWords;
                    break;
            }

            CharadesWordResponse[] response = words
                .Select(word => new CharadesWordResponse
                {
                    Difficulty = difficulty,
                    EN_US = word
                })
                .ToArray();

            Console.WriteLine(
                $"[CHARADES WORDS] category={category} " +
                $"difficulty={difficulty} count={response.Length}");

            return Ok(response);
        }

        [HttpGet("/autoloc/v2/string")]
        [HttpPost("/autoloc/v2/string")]
        public IActionResult GetAutoLocalizedStrings()
        {

            Console.WriteLine(
                $"[AUTOLOC] method={Request.Method} query={Request.QueryString} " +
                $"contentLength={Request.ContentLength ?? 0}");

            return Ok(new
            {
                Strings = new Dictionary<string, string>(),
                Translations = new Dictionary<string, string>(),
                Success = true
            });
        }

        [HttpPost("/v1/batch/rudderstack")]
        [RequestSizeLimit(4 * 1024 * 1024)]
        public IActionResult AcceptRudderStackBatch()
        {

            return NoContent();
        }

        private static string NormalizeCategory(string? category)
        {
            if (string.IsNullOrWhiteSpace(category))
                return "charadeseasy";

            return new string(category
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
        }

        private sealed class CharadesWordResponse
        {
            [JsonPropertyName("EN_US")]
            public string EN_US { get; set; } = string.Empty;

            [JsonPropertyName("Difficulty")]
            public int Difficulty { get; set; }
        }
    }
}
