using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounting_System.Models
{
    public class Customer
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int CustomerId { get; set; }

        [Display(Name = "Customer No")]
        public int Number { get; set; }

        [Required]
        [Display(Name = "Customer Name")]
        [StringLength(100)]
        public string CustomerName { get; set; }

        [Required]
        [Display(Name = "Customer Address")]
        [StringLength(200)]
        public string CustomerAddress { get; set; }

        [Required]
        [Display(Name = "TIN No")]
        [RegularExpression(@"\d{3}-\d{3}-\d{3}-\d{5}", ErrorMessage = "Invalid TIN number format.")]
        [StringLength(20)]
        public string CustomerTin { get; set; }

        [Required]
        [Display(Name = "Business Style")]
        [StringLength(100)]
        public string? BusinessStyle { get; set; }

        [Required]
        [Display(Name = "Payment Terms")]
        [StringLength(10)]
        public string CustomerTerms { get; set; }

        [Required]
        [Display(Name = "Customer Type")]
        [StringLength(20)]
        public string CustomerType { get; set; }

        [Required]
        [Display(Name = "Creditable Withholding VAT 2306 ")]
        public bool WithHoldingVat { get; set; }

        [Required]
        [Display(Name = "Creditable Withholding Tax 2307")]
        public bool WithHoldingTax { get; set; }

        [Display(Name = "Created By")]
        [StringLength(50)]
        public string? CreatedBy { get; set; }

        [Display(Name = "Created Date")]
        [Column(TypeName = "timestamp without time zone")]
        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public int OriginalCustomerId { get; set; }

        [StringLength(13)]
        public string? OriginalCustomerNumber { get; set; }

        [Required]
        [Display(Name = "Zip Code")]
        [StringLength(10)]
        public string ZipCode { get; set; } = String.Empty;
    }
}
