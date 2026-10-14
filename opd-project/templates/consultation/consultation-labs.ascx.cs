using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.UI.WebControls;
using OpdProject.Models;
using OpdProject.Services;
using OpdProject.Web;

namespace OpdProject.templates.consultation
{
    /// <summary>Lab / imaging orders: favourites, pathway suggestions, catalog search, custom tests, ★ favourites, skip.</summary>
    public partial class consultation_labs : ConsultationSection
    {
        private HashSet<string> _favorites = new HashSet<string>();

        protected override string SectionKey { get { return "labs"; } }

        /// <summary>Test names suggested by the branched-flow engine.</summary>
        public List<string> SuggestedLabs { get; set; }

        public override void Bind()
        {
            OrderingHolder.Visible = !Host.IsLocked;
            var favorites = CatalogService.ListFavorites(Host.DoctorId);
            _favorites = new HashSet<string>(favorites.Select(f => f.TestId));
            FavoritesRepeater.DataSource = favorites;
            FavoritesRepeater.DataBind();
            NoFavorites.Visible = favorites.Count == 0;

            var ordered = ConsultationService.ListOrders(ConsultationId);
            var orderedIds = new HashSet<string>(ordered.Select(o => o.TestId));
            var suggested = (SuggestedLabs ?? new List<string>())
                .Select(name => CatalogService.SearchLabTests(name).FirstOrDefault(t => string.Equals(t.TestName, name, StringComparison.OrdinalIgnoreCase)))
                .Where(t => t != null && !orderedIds.Contains(t.TestId)).ToList();
            SuggestedHolder.Visible = suggested.Count > 0;
            SuggestedRepeater.DataSource = suggested;
            SuggestedRepeater.DataBind();

            OrdersRepeater.DataSource = ordered;
            OrdersRepeater.DataBind();
            NoOrders.Visible = ordered.Count == 0;
        }

        protected override void ApplyFormConfig()
        {
            FavoritesHolder.Visible = FieldOn("favorites");
            if (!FieldOn("suggested")) SuggestedHolder.Visible = false;
            SearchHolder.Visible = FieldOn("search");
            CustomPanel.Visible = FieldOn("custom");
            ApplyOptions("custom", CustomTestType);
            SkipLabsButton.Visible = FieldOn("skip");
        }

        protected bool IsFavorite(string testId) { return _favorites.Contains(testId); }

        protected void LabSearchButton_Click(object sender, EventArgs e)
        {
            BindSearch();
            Host.ActivateStep("labs");
        }

        private void BindSearch()
        {
            _favorites = new HashSet<string>(CatalogService.ListFavorites(Host.DoctorId).Select(f => f.TestId));
            var term = LabSearchInput.Text.Trim();
            var results = CatalogService.SearchLabTests(term);
            SearchResults.DataSource = results;
            SearchResults.DataBind();
            SearchHint.Text = term.Length < 2 ? "<div class=\"text-xs text-muted\">Type at least 2 characters to search.</div>"
                : results.Count == 0 ? "<div class=\"text-xs text-muted\">No matching tests — add it as a custom test below.</div>" : "";
        }

        protected void Order_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            Host.ActivateStep("labs");
            var testId = (string)e.CommandArgument;
            if (e.CommandName == "Favorite")
            {
                bool nowFavorite = CatalogService.ToggleFavorite(Host.DoctorId, testId);
                AppPage.ToastSuccess(nowFavorite ? "Added to favorites" : "Removed from favorites");
                Bind();
                BindSearch();
                return;
            }
            if (e.CommandName != "Order" || !EnsureNotLocked()) return;
            var test = CatalogService.GetLabTest(testId);
            if (test == null) return;
            PlaceOrder(test);
        }

        private void PlaceOrder(LabTest test)
        {
            var order = ConsultationService.AddOrder(Visit, test);
            if (order == null)
            {
                AppPage.ToastWarning("Already ordered", test.TestName + " is already in the order list.");
                return;
            }
            Audit("CREATE", "InvestigationOrder", test.TestName);
            AppPage.ToastSuccess(test.TestName + " ordered");
            Host.RefreshAll();
        }

        protected void AddCustomTestButton_Click(object sender, EventArgs e)
        {
            Host.ActivateStep("labs");
            if (!EnsureNotLocked()) return;
            var name = CustomTestName.Text.Trim();
            if (name.Length < 2)
            {
                AppPage.ToastWarning("Enter a test name");
                return;
            }
            var test = CatalogService.AddCustomLabTest(HospitalId, name, CustomTestType.SelectedValue);
            CustomTestName.Text = "";
            PlaceOrder(test);
        }

        protected void SkipLabsButton_Click(object sender, EventArgs e)
        {
            ConsultationService.SkipStep(ConsultationId, "labs");
            AppPage.ToastSuccess("Marked as not needed");
            Host.ActivateStep("cpoe");
            Host.RefreshAll();
        }

        protected void OrdersRepeater_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            Host.ActivateStep("labs");
            if (e.CommandName != "Remove" || !EnsureNotLocked()) return;
            if (ConsultationService.RemoveOrder((string)e.CommandArgument))
            {
                Audit("DELETE", "InvestigationOrder", (string)e.CommandArgument);
                AppPage.ToastSuccess("Order removed");
            }
            else AppPage.ToastWarning("The lab has already picked up this order — it can no longer be removed");
            Host.RefreshAll();
        }
    }
}
