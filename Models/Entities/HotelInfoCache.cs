using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickNBook.Api.Models.Entities
{
    public class HotelInfoCache
    {
        [Key]
        [MaxLength(100)]
        public string HotelCode { get; set; } = string.Empty;

        [MaxLength(255)]
        public string HotelName { get; set; } = string.Empty;

        public double StarRating { get; set; }

        [MaxLength(500)]
        public string HotelURL { get; set; } = string.Empty;

        [MaxLength(500)]
        public string HotelPicture { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string Address { get; set; } = string.Empty;

        [MaxLength(100)]
        public string City { get; set; } = string.Empty;

        [MaxLength(100)]
        public string State { get; set; } = string.Empty;

        [MaxLength(50)]
        public string PinCode { get; set; } = string.Empty;

        [MaxLength(100)]
        public string CountryName { get; set; } = string.Empty;

        [MaxLength(100)]
        public string HotelContactNo { get; set; } = string.Empty;

        [MaxLength(100)]
        public string FaxNumber { get; set; } = string.Empty;

        [MaxLength(255)]
        public string Email { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Latitude { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Longitude { get; set; } = string.Empty;

        // Large Text / JSON Payload Fields
        [Column(TypeName = "longtext")]
        public string OtherDetails { get; set; } = string.Empty;

        [Column(TypeName = "longtext")]
        public string HotelPolicy { get; set; } = string.Empty;

        [Column(TypeName = "longtext")]
        public string SpecialInstructions { get; set; } = string.Empty;

        [Column(TypeName = "longtext")]
        public string RoomData { get; set; } = string.Empty;

        [Column(TypeName = "longtext")]
        public string RoomFacilities { get; set; } = string.Empty;

        [Column(TypeName = "longtext")]
        public string Services { get; set; } = string.Empty;

        // JSON Serialized Collections
        [Column(TypeName = "longtext")]
        public string DescriptionJson { get; set; } = "[]";

        [Column(TypeName = "longtext")]
        public string PolicyAndInstructionJson { get; set; } = "[]";

        [Column(TypeName = "longtext")]
        public string AttractionsJson { get; set; } = "[]";

        [Column(TypeName = "longtext")]
        public string HotelFacilitiesJson { get; set; } = "[]";

        [Column(TypeName = "longtext")]
        public string ImagesJson { get; set; } = "[]";

        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}
