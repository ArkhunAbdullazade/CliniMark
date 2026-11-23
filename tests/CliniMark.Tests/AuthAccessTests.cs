using System.ComponentModel.DataAnnotations;
using CliniMark.Core.Services;
using CliniMark.Presentation.Dtos;
using Xunit;

namespace CliniMark.Tests;

public class AuthAccessTests
{
    [Theory]
    [InlineData("specialist@uniklinik.example.de")]
    [InlineData("researcher@hospital.example.org")]
    [InlineData("doctor@clinic.example")]
    public void SignupAcceptsWorkplaceEmailAddresses(string email)
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

    [Fact]
    public void BodyRegionIsOptionalButSpecialityIsRequired()
    {
        foreach (var empty in new string[]?[] { null, [] })
        {
            var filters = ValidFilters();
            filters.BodyRegion = empty;
            Assert.Empty(Validate(filters));
            filters = ValidFilters();
            filters.Speciality = empty;
            Assert.Equal([ImageFiltersDto.SpecialityRequired], Validate(filters).Select(error => error.ErrorMessage));
        }
        var blank = ValidFilters();
        blank.Speciality = ["  "];
        Assert.Equal([ImageFiltersDto.SpecialityRequired], Validate(blank).Select(error => error.ErrorMessage));
    }

    [Theory]
    [InlineData("Speciality")]
    [InlineData("BodyRegion")]
    public void SelectionsRejectBlankOrUnlistedOptions(string property)
    {
        foreach (var selection in new string[][] { ["invented option"], ["ct", ""], ["cardiology", "   "] })
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
            Assert.Equal([ImageFiltersDto.ModalityRequired, ImageFiltersDto.SpecialityRequired], Validate(nothing).Select(error => error.ErrorMessage));
            var others = new ImageFiltersDto { Speciality = ["cardiology"], BodyRegion = ["chest and thorax"], ImageModality = empty };
            Assert.Equal([ImageFiltersDto.ModalityRequired], Validate(others).Select(error => error.ErrorMessage));
        }
    }

    [Fact]
    public void SignupNeedsAnImageModalityAndASpecialityButBodyRegionIsOptional()
    {
        var signup = ValidSignup();
        signup.BodyRegion = [];
        Assert.Empty(Validate(signup));
        signup.Speciality = null;
        Assert.Contains(Validate(signup), error => error.ErrorMessage == ImageFiltersDto.SpecialityRequired);
        signup.Speciality = ["cardiology"];
        signup.ImageModality = [];
        Assert.Contains(Validate(signup), error => error.ErrorMessage == ImageFiltersDto.ModalityRequired);
    }

    [Theory]
    [InlineData("doctor@gmail.com")]
    [InlineData("Doctor@GoogleMail.com")]
    [InlineData("doctor@hotmail.co.uk")]
    [InlineData("doctor@outlook.com")]
    [InlineData("doctor@live.de")]
    [InlineData("doctor@yahoo.com.br")]
    [InlineData("doctor@icloud.com")]
    [InlineData("doctor@proton.me")]
    [InlineData("doctor@gmx.net")]
    [InlineData("doctor@web.de")]
    [InlineData("doctor@t-online.de")]
    [InlineData("doctor@ukr.net")]
    [InlineData("doctor@mail.ru")]
    [InlineData("doctor@yandex.ru")]
    [InlineData("doctor@mail.az")]
    [InlineData("doctor@gmail.com.")]
    public void SignupRefusesPersonalEmailServices(string email)
    {
        Assert.True(InstitutionalEmail.IsPersonal(email));
        var signup = ValidSignup();
        signup.Email = email.TrimEnd('.');
        Assert.Contains(Validate(signup), error => error.ErrorMessage == InstitutionalEmail.Required
            && error.MemberNames.Contains(nameof(SignupDto.Email)));
    }

    [Theory]
    [InlineData("doctor@charite.de")]
    [InlineData("doctor@hpi.uni-potsdam.de")]
    [InlineData("doctor@nhs.net")]
    [InlineData("doctor@live.nhs.uk")]
    [InlineData("doctor@mail.hospital.org")]
    [InlineData("doctor@outlook.university.edu")]
    [InlineData("doctor@med.uni-x.de")]
    [InlineData("doctor@amu.edu.az")]
    public void SignupAcceptsInstitutionalAddresses(string email)
    {
        Assert.False(InstitutionalEmail.IsPersonal(email));
        var signup = ValidSignup();
        signup.Email = email;
        Assert.Empty(Validate(signup));
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
        Email = "specialist@clinic.example.org", FullName = "Research Specialist",
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
