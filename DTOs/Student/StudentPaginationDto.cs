namespace Idara.API.DTOs.Student
{
    public class StudentPaginationDto
    {
        public int Page { get; set; } = 1;
        // 25 et non 10 depuis le 2026-07-28 : la compression des réponses étant
        // désormais active côté nginx, une page de 25 élèves coûte à peine plus
        // d'octets qu'une page de 10 — mais épargne deux allers-retours, et
        // c'est la LATENCE qui domine en 3G (~0,5 s par échange depuis Dakar).
        public int PageSize { get; set; } = 25;
        public string? Search { get; set; }
        public string? SortBy { get; set; }
        public bool SortDescending { get; set; } = true;
    }
}
