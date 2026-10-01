using System;
namespace MyApp.Domain.Entities {
    public class User {
        public Guid Id { get; set; }
        public required string FirstName { get; set; }
        public required string MiddleName { get; set; }
        public required string LastName { get; set; }
        public required string UserName { get; set; }
        public required string Email { get; set; }
        public required string PasswordHash { get; set; }
        public Guid PositionId { get; set; }
        public Profession Profession { get; set; } = null!;
        public required string Role { get; set; }
        public string Permissions { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public byte[]? AvatarContent { get; set; }
        public string? AvatarContentType { get; set; }
    }
}
