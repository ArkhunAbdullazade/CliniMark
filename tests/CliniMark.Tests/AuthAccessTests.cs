using System.ComponentModel.DataAnnotations;
using CliniMark.Presentation.Dtos;
using Xunit;

namespace CliniMark.Tests;

public class AuthAccessTests
{
    [Theory]
    [InlineData("specialist@gmail.com")]
    [InlineData("researcher@outlook.com")]
    [InlineData("doctor@clinic.example")]
    public void SignupAcceptsOrdinaryEmailAddresses(string email)
    {
        var signup = ValidSignup();
        signup.Email = email;
        Assert.Empty(Validate(signup));
    }

    [Fact]
    public void SignupStillRequiresAValidEmailAndPersonalPassword()
    {
        var signup = ValidSignup();
        signup.Email = "invalid-email";
        signup.Password = "";
        Assert.Contains(Validate(signup), error => error.MemberNames.Contains(nameof(SignupDto.Email)));
        Assert.Contains(Validate(signup), error => error.MemberNames.Contains(nameof(SignupDto.Password)));
    }

    [Theory]
    [InlineData("Speciality")]
    [InlineData("BodyRegion")]
    public void SpecialityAndBodyRegionAreOptionalButRejectBlankOrUnlistedSelections(string property)
    {
        foreach (var empty in new string[]?[] { null, [] })
        {
            var filters = ValidFilters();
            typeof(ImageFiltersDto).GetProperty(property)!.SetValue(filters, empty);
            Assert.Empty(Validate(filters));
        }
        foreach (var selection in new string[][] { [""], ["   "], ["invented option"], ["ct", ""] })
        {
            var filters = ValidFilters();
            typeof(ImageFiltersDto).GetProperty(property)!.SetValue(filters, selection);
            Assert.Contains(Validate(filters), error => error.MemberNames.Contains(property));
        }
    }

    [Fact]
    public void ImageModalityIsRequiredEvenWhenOtherListsAreChosen()
    {
        foreach (var empty in new string[]?[] { null, [], ["  "] })
        {
            var nothing = new ImageFiltersDto { Speciality = null, BodyRegion = null, ImageModality = empty };
            Assert.Equal([ImageFiltersDto.ModalityRequired], Validate(nothing).Select(error => error.ErrorMessage));
            var others = new ImageFiltersDto { Speciality = ["cardiology"], BodyRegion = ["chest and thorax"], ImageModality = empty };
            Assert.Equal([ImageFiltersDto.ModalityRequired], Validate(others).Select(error => error.ErrorMessage));
        }
    }

    [Fact]
    public void SignupNeedsAnImageModalityButSpecialityAndBodyRegionAreOptional()
    {
        var signup = ValidSignup();
        signup.Speciality = null;
        signup.BodyRegion = [];
        Assert.Empty(Validate(signup));
        signup.Speciality = ["cardiology"];
        signup.ImageModality = [];
        Assert.Contains(Validate(signup), error => error.ErrorMessage == ImageFiltersDto.ModalityRequired);
    }

    [Fact]
    public void SignupDoesNotAskForClinicalExperience()
    {
        Assert.Null(typeof(SignupDto).GetProperty("ClinicalExperience"));
    }

    [Fact]
    public void AllListsAcceptMultipleSelections()
    {
        var filters = new ImageFiltersDto
        {
            Speciality = ["cardiology", "dermatology"],
            BodyRegion = ["Abdomen", "Chest and Thorax"],
            ImageModality = ["ct", "Microscopy"]
        };
        Assert.Empty(Validate(filters));
    }

    [Fact]
    public void StoredSelectionsAreTrimmedNormalizedAndDeduplicated()
    {
        Assert.Equal("cardiology,dermatology", ImageFiltersDto.JoinSelection([" Cardiology ", "cardiology", "DERMATOLOGY"]));
    }

    private static ImageFiltersDto ValidFilters() => new()
    {
        Speciality = ["cardiology"], BodyRegion = ["chest and thorax"], ImageModality = ["ct"]
    };

    private static SignupDto ValidSignup() => new()
    {
        Email = "specialist@gmail.com", FullName = "Research Specialist",
        Password = "Participant1!", ConfirmPassword = "Participant1!",
        Position = "specialist", University = "Community clinic",
        Speciality = ["cardiology"], BodyRegion = ["chest and thorax"], ImageModality = ["ct"]
    };

    private static List<ValidationResult> Validate(object model)
    {
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), errors, validateAllProperties: true);
        return errors;
    }
}
