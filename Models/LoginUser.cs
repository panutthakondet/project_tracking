using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracking.Models
{
    [Table("login_user")]
    public class LoginUser
    {
        [Key]
        [Column("user_id")]
        public int UserId { get; set; }

        [Required]
        [Column("username")]
        public string Username { get; set; } = "";

        [Required]
        [Column("password")]
        public string Password { get; set; } = "";

        [Column("role")]
        public string Role { get; set; } = "USER";

        [Column("status")]
        public string Status { get; set; } = "ACTIVE";

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        // ✅ เพิ่มใหม่ตามโครงสร้างตาราง
        [Column("email")]
        public string? Email { get; set; }

        [Column("email_verified")]
        public bool EmailVerified { get; set; } // tinyint(1)

        [Column("verify_token_hash")]
        public string? VerifyTokenHash { get; set; } // char(64)

        [Column("verify_token_expire")]
        public DateTime? VerifyTokenExpire { get; set; }
    }
}