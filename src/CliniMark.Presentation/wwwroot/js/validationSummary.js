(function () {
  if (!window.jQuery || !window.jQuery.validator) {
    return;
  }

  const $ = window.jQuery;

  // Leaving a corrected field clears the error box and moves the form up. If that happened between press and release, the
  // click would land elsewhere and be lost, so a mouse press on a control keeps focus in the field. Keyboard use is unaffected.
  $(document).on("mousedown", ".auth-card a, .auth-card button", function (event) {
    event.preventDefault();
  });

  $.validator.setDefaults({
    errorElement: "span",
    errorClass: "text-danger",
    errorPlacement: function () {
      return;
    },
    highlight: function (element) {
      $(element).addClass("input-validation-error");
    },
    unhighlight: function (element) {
      $(element).removeClass("input-validation-error");
    },
    showErrors: function (errorMap, errorList) {
      this.defaultShowErrors();

      let summary = $(".validation-summary-errors, .validation-summary-valid").first();
      if (summary.length === 0) {
        summary = $("<div class='validation-summary-errors'><ul></ul></div>");
        $(".mb-3.w-100").first().html(summary);
      }

      let list = summary.find("ul");
      if (list.length === 0) {
        summary.html("<ul></ul>");
        list = summary.find("ul");
      }

      list.empty();

      if (errorList.length === 0) {
        summary.removeClass("validation-summary-errors").addClass("validation-summary-valid");
        return;
      }

      errorList.forEach(function (error) {
        $("<li></li>").text(error.message).appendTo(list);
      });

      summary.removeClass("validation-summary-valid").addClass("validation-summary-errors");
    }
  });
})();
