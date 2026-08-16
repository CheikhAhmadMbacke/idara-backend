using Idara.API.Common.Extensions;
using Idara.API.Common.Utilities;
using Idara.API.Constants;
using Idara.API.Data;
using Idara.API.DTOs.Student;
using Idara.API.Enums;
using Idara.API.Models;
using Idara.API.Options;
using Idara.API.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Idara.API.Services
{
    public class StudentService : IStudentService
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<StudentService> _logger;
        private readonly IEmailService _emailService;
        private readonly INotificationService _notif;
        private readonly IInvoiceRepricingService _repricing;
        private readonly UploadSettings _uploads;

        public StudentService(
            AppDbContext context,
            IWebHostEnvironment env,
            ILogger<StudentService> logger,
            IEmailService emailService,
            INotificationService notif,
            IInvoiceRepricingService repricing,
            IOptions<UploadSettings> uploads)
        {
            _context = context;
            _env = env;
            _logger = logger;
            _emailService = emailService;
            _notif = notif;
            _repricing = repricing;
            _uploads = uploads.Value;
        }

        public async Task<StudentListResponseDto> GetStudentsAsync(
            int schoolId, StudentPaginationDto pagination,
            IReadOnlyList<int>? restrictToClassIds = null)
        {
            var query = _context.Students
                .Include(s => s.Class)
                .Include(s => s.StudentGuardians)
                    .ThenInclude(sg => sg.Guardian)
                .Where(s => s.SchoolId == schoolId && !s.IsDeleted);

            // Périmètre de l'appelant EN PREMIER : un enseignant ne voit que les
            // élèves de ses classes. Placé avant tout le reste pour que la
            // recherche, les compteurs d'onglets, le total et la pagination
            // portent tous sur son seul périmètre (§141).
            if (restrictToClassIds != null)
            {
                query = restrictToClassIds.Count == 0
                    ? query.Where(s => false)
                    : query.Where(s => s.ClassId != null && restrictToClassIds.Contains(s.ClassId.Value));
            }

            if (!string.IsNullOrWhiteSpace(pagination.Search))
            {
                var search = pagination.Search.ToLower();
                query = query.Where(s =>
                    s.FirstName.ToLower().Contains(search) ||
                    s.LastName.ToLower().Contains(search) ||
                    (s.StudentNumber != null && s.StudentNumber.ToLower().Contains(search)));
            }

            // ----- Filtres (2026-08-09) -----
            // Appliqués AVANT le Count et la pagination : sans quoi le total et
            // le nombre de pages porteraient sur la liste non filtrée, et une
            // page pourrait revenir à moitié vide.
            if (pagination.HasCustomFee == true)
                query = query.Where(s => _context.StudentFeeOverrides.Any(o => o.StudentId == s.Id));
            else if (pagination.HasCustomFee == false)
                query = query.Where(s => !_context.StudentFeeOverrides.Any(o => o.StudentId == s.Id));

            if (pagination.ClassId.HasValue)
                query = query.Where(s => s.ClassId == pagination.ClassId.Value);
            else if (pagination.WithoutClass == true)
                query = query.Where(s => s.ClassId == null);

            // Les compteurs des onglets sont pris ICI, sur la requête filtrée
            // par tout SAUF le régime : un onglet doit annoncer le nombre
            // d'élèves qu'on verra en tapant dessus. Compter avant les filtres
            // de classe ou de recherche ferait afficher « Interne (43) »
            // au-dessus d'une liste de 4 élèves.
            var counts = await query
                .GroupBy(s => s.BoardingStatus)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            var boardingCounts = new BoardingCountsDto
            {
                Total = counts.Sum(c => c.Count),
                Boarding = counts.FirstOrDefault(c => c.Status == Enums.BoardingStatus.Boarding)?.Count ?? 0,
                HalfBoarding = counts.FirstOrDefault(c => c.Status == Enums.BoardingStatus.HalfBoarding)?.Count ?? 0,
                Day = counts.FirstOrDefault(c => c.Status == Enums.BoardingStatus.Day)?.Count ?? 0,
                Unset = counts.FirstOrDefault(c => c.Status == null)?.Count ?? 0,
            };

            // Le filtre de régime, lui, s'applique APRÈS le comptage.
            if (pagination.BoardingStatus.HasValue)
                query = query.Where(s => s.BoardingStatus == pagination.BoardingStatus.Value);
            else if (pagination.BoardingStatusUnset == true)
                query = query.Where(s => s.BoardingStatus == null);

            query = pagination.SortBy switch
            {
                "nom" => pagination.SortDescending
                    ? query.OrderByDescending(s => s.LastName).ThenBy(s => s.FirstName)
                    : query.OrderBy(s => s.LastName).ThenBy(s => s.FirstName),
                "classe" => pagination.SortDescending
                    ? query.OrderByDescending(s => s.Class!.Name)
                    : query.OrderBy(s => s.Class!.Name),
                _ => pagination.SortDescending
                    ? query.OrderByDescending(s => s.EnrollmentDate)
                    : query.OrderBy(s => s.EnrollmentDate)
            };

            // Bornes défensives (2026-07-28). Rien ne limitait la taille de page
            // demandée : `?pageSize=100000` faisait charger et sérialiser toute
            // la base en une réponse. 500 laisse largement la place aux écrans
            // qui chargent une classe entière d'un coup (ils demandent 200) tout
            // en plafonnant le coût d'une requête malveillante ou maladroite.
            var page = pagination.Page < 1 ? 1 : pagination.Page;
            var pageSize = Math.Clamp(pagination.PageSize, 1, 500);

            var totalCount = await query.CountAsync();
            var students = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Tarifs personnalisés de la PAGE en UNE requête (pas de N+1) :
            // c'est ce qui alimente le badge « tarif personnalisé » de la liste.
            var pageIds = students.Select(s => s.Id).ToList();
            var overrides = await _context.StudentFeeOverrides
                .Where(o => pageIds.Contains(o.StudentId))
                .ToDictionaryAsync(o => o.StudentId, o => o);

            return new StudentListResponseDto
            {
                Items = students
                    .Select(s => MapToResponseDto(s, overrides.GetValueOrDefault(s.Id)))
                    .ToList(),
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                BoardingCounts = boardingCounts
            };
        }

        public async Task<StudentResponseDto?> GetStudentByIdAsync(int id, int schoolId)
        {
            var student = await _context.Students
                .Include(s => s.Class)
                .Include(s => s.StudentGuardians)
                    .ThenInclude(sg => sg.Guardian)
                .Include(s => s.Documents)
                .FirstOrDefaultAsync(s => s.Id == id && s.SchoolId == schoolId && !s.IsDeleted);

            if (student == null) return null;

            var fee = await _context.StudentFeeOverrides
                .FirstOrDefaultAsync(o => o.StudentId == student.Id);

            return MapToResponseDto(student, fee);
        }

        public async Task<StudentResponseDto> CreateStudentAsync(int schoolId, int currentUserId, StudentCreateDto dto)
        {
            var school = await _context.Schools.FindAsync(schoolId)
                ?? throw new InvalidOperationException("École introuvable.");

            // Validation classe (doit appartenir à la même école)
            if (dto.ClassId.HasValue)
            {
                var classExists = await _context.Classes
                    .AnyAsync(c => c.Id == dto.ClassId.Value && c.SchoolId == schoolId && !c.IsDeleted);
                if (!classExists)
                    throw new InvalidOperationException("Classe introuvable pour cette école.");
            }

            string? photoUrl = null;
            if (!string.IsNullOrWhiteSpace(dto.PhotoBase64))
            {
                photoUrl = await SavePhotoAsync(dto.PhotoBase64);
            }

            var now = DateTime.UtcNow;
            var student = new Student
            {
                FirstName = dto.FirstName,
                LastName = dto.LastName,
                MiddleName = NullIfEmpty(dto.MiddleName),
                Gender = dto.Gender,
                DateOfBirth = dto.DateOfBirth.ToUtcSafe(),
                PlaceOfBirth = NullIfEmpty(dto.PlaceOfBirth),
                Nationality = NullIfEmpty(dto.Nationality),
                PhotoUrl = photoUrl,

                Address = NullIfEmpty(dto.Address),
                City = NullIfEmpty(dto.City),
                Region = NullIfEmpty(dto.Region),
                Country = NullIfEmpty(dto.Country),

                EnrollmentDate = (dto.EnrollmentDate ?? DateTime.UtcNow).ToUtcSafe(),
                ClassId = dto.ClassId,
                StudentNumber = NullIfEmpty(dto.StudentNumber),
                PreviousSchool = NullIfEmpty(dto.PreviousSchool),
                PreviousClass = NullIfEmpty(dto.PreviousClass),
                TransferReason = NullIfEmpty(dto.TransferReason),
                BoardingStatus = dto.BoardingStatus,

                BloodType = NullIfEmpty(dto.BloodType),
                Allergies = NullIfEmpty(dto.Allergies),
                ChronicConditions = NullIfEmpty(dto.ChronicConditions),
                CurrentMedications = NullIfEmpty(dto.CurrentMedications),
                DoctorName = NullIfEmpty(dto.DoctorName),
                DoctorPhone = NullIfEmpty(dto.DoctorPhone),
                EmergencyContactName = NullIfEmpty(dto.EmergencyContactName),
                EmergencyContactPhone = NullIfEmpty(dto.EmergencyContactPhone),
                EmergencyContactRelation = NullIfEmpty(dto.EmergencyContactRelation),

                FatherFullName = NullIfEmpty(dto.FatherFullName),
                FatherPhone = NullIfEmpty(dto.FatherPhone),
                FatherEmail = NullIfEmpty(dto.FatherEmail),
                FatherProfession = NullIfEmpty(dto.FatherProfession),
                MotherFullName = NullIfEmpty(dto.MotherFullName),
                MotherPhone = NullIfEmpty(dto.MotherPhone),
                MotherEmail = NullIfEmpty(dto.MotherEmail),
                MotherProfession = NullIfEmpty(dto.MotherProfession),

                Notes = NullIfEmpty(dto.Notes),

                SchoolId = schoolId,
                IsDeleted = false,
                CreatedAt = now
            };

            // Transaction : Student + StudentNumber + Guardians + Documents
            // doivent être atomiques (sinon StudentNumber peut rester null si crash).
            using var tx = await _context.Database.BeginTransactionAsync();

            _context.Students.Add(student);
            await _context.SaveChangesAsync();

            // Génération auto du matricule si non fourni (post-Id, dans la même transaction).
            if (string.IsNullOrWhiteSpace(student.StudentNumber))
            {
                student.StudentNumber = $"E{schoolId:D3}-{student.Id:D5}";
                await _context.SaveChangesAsync();
            }

            // Tarif personnalisé saisi sur la fiche d'inscription. Écrit dans la
            // MÊME table que l'écran « Tarif par classe » (StudentFeeOverride),
            // jamais dans un second champ parallèle : deux sources de tarif
            // finiraient par se contredire sans que personne ne le voie.
            if (dto.MonthlyFeeFcfa is > 0)
            {
                _context.StudentFeeOverrides.Add(new StudentFeeOverride
                {
                    StudentId = student.Id,
                    SchoolId = schoolId,
                    AmountFcfa = dto.MonthlyFeeFcfa.Value,
                    Reason = NullIfEmpty(dto.MonthlyFeeReason),
                    CreatedById = currentUserId,
                    CreatedAt = now
                });
            }

            // Liens responsables. On collecte les NOUVEAUX responsables (code +
            // requête SMS) pour l'affichage à l'école ET l'envoi POST-COMMIT.
            var newGuardians = new List<NewGuardianResult>();
            foreach (var guardianDto in dto.Guardians)
            {
                var (guardian, newG) = await GetOrCreateGuardianAsync(guardianDto, schoolId, school.Name ?? "Idara");
                if (newG != null) newGuardians.Add(newG);
                _context.StudentGuardians.Add(new StudentGuardian
                {
                    StudentId = student.Id,
                    GuardianId = guardian.Id,
                    Relationship = guardianDto.Relationship,
                    IsPrimaryGuardian = guardianDto.IsPrimaryGuardian
                });
            }

            // Documents
            foreach (var docDto in dto.Documents)
            {
                var saved = await SaveDocumentFileAsync(docDto);
                if (saved == null) continue;
                _context.StudentDocuments.Add(new StudentDocument
                {
                    StudentId = student.Id,
                    Type = docDto.Type,
                    FileName = saved.Value.fileName,
                    OriginalFileName = docDto.OriginalFileName,
                    FilePath = saved.Value.filePath,
                    ContentType = saved.Value.contentType,
                    FileSize = saved.Value.size,
                    UploadedAt = now,
                    Notes = docDto.Notes
                });
            }

            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            // Pas de re-tarification ici : un élève qui vient d'être créé ne peut
            // pas encore avoir de facture. Sa première facture sera générée par
            // le cron, qui résout le tarif via le même FeeResolver.

            // POST-COMMIT : envoi SMS de bienvenue best-effort. Si la tx avait
            // rollback, aucun SMS ne serait parti (plus de compte fantôme).
            foreach (var g in newGuardians)
                await _notif.SendSmsAsync(g.Sms);

            var created = await GetStudentByIdAsync(student.Id, schoolId);
            created!.NewGuardianCredentials = newGuardians.Select(g => g.Credential).ToList();
            return created;
        }

        public async Task<StudentResponseDto?> UpdateStudentAsync(int schoolId, int currentUserId, StudentUpdateDto dto)
        {
            var student = await _context.Students
                .Include(s => s.StudentGuardians)
                .FirstOrDefaultAsync(s => s.Id == dto.Id && s.SchoolId == schoolId && !s.IsDeleted);

            if (student == null) return null;

            // Les 3 entrées de la hiérarchie tarifaire portées par la fiche
            // élève. On mémorise leur valeur AVANT modification pour ne
            // re-tarifer que si l'une d'elles a réellement changé (une simple
            // correction d'orthographe ne doit pas relancer un balayage des
            // factures).
            var previousClassId = student.ClassId;
            var previousBoarding = student.BoardingStatus;

            // Validation ClassId
            if (dto.ClassId.HasValue && dto.ClassId.Value > 0)
            {
                var classExists = await _context.Classes
                    .AnyAsync(c => c.Id == dto.ClassId.Value && c.SchoolId == schoolId && !c.IsDeleted);
                if (!classExists)
                    throw new InvalidOperationException("Classe introuvable pour cette école.");
                student.ClassId = dto.ClassId.Value;
            }

            // Régime d'hébergement. Une valeur nulle ne pouvant pas distinguer
            // « ne pas toucher » de « vider », le vidage passe par un drapeau
            // explicite (cf. StudentUpdateDto).
            if (dto.ClearBoardingStatus)
                student.BoardingStatus = null;
            else if (dto.BoardingStatus.HasValue)
                student.BoardingStatus = dto.BoardingStatus.Value;

            // PATCH partiel : un champ string null ⇒ pas touché. Pour vider, envoyer "".
            if (dto.FirstName != null)        student.FirstName = dto.FirstName;
            if (dto.LastName != null)         student.LastName = dto.LastName;
            if (dto.MiddleName != null)       student.MiddleName = NullIfEmpty(dto.MiddleName);
            if (dto.Gender.HasValue)          student.Gender = dto.Gender.Value;
            if (dto.DateOfBirth.HasValue)     student.DateOfBirth = dto.DateOfBirth.Value.ToUtcSafe();
            if (dto.PlaceOfBirth != null)     student.PlaceOfBirth = NullIfEmpty(dto.PlaceOfBirth);
            if (dto.Nationality != null)      student.Nationality = NullIfEmpty(dto.Nationality);

            if (dto.Address != null)          student.Address = NullIfEmpty(dto.Address);
            if (dto.City != null)             student.City = NullIfEmpty(dto.City);
            if (dto.Region != null)           student.Region = NullIfEmpty(dto.Region);
            if (dto.Country != null)          student.Country = NullIfEmpty(dto.Country);

            if (dto.EnrollmentDate.HasValue)  student.EnrollmentDate = dto.EnrollmentDate.Value.ToUtcSafe();
            if (dto.StudentNumber != null)    student.StudentNumber = NullIfEmpty(dto.StudentNumber);
            if (dto.PreviousSchool != null)   student.PreviousSchool = NullIfEmpty(dto.PreviousSchool);
            if (dto.PreviousClass != null)    student.PreviousClass = NullIfEmpty(dto.PreviousClass);
            if (dto.TransferReason != null)   student.TransferReason = NullIfEmpty(dto.TransferReason);

            if (dto.BloodType != null)              student.BloodType = NullIfEmpty(dto.BloodType);
            if (dto.Allergies != null)              student.Allergies = NullIfEmpty(dto.Allergies);
            if (dto.ChronicConditions != null)      student.ChronicConditions = NullIfEmpty(dto.ChronicConditions);
            if (dto.CurrentMedications != null)     student.CurrentMedications = NullIfEmpty(dto.CurrentMedications);
            if (dto.DoctorName != null)             student.DoctorName = NullIfEmpty(dto.DoctorName);
            if (dto.DoctorPhone != null)            student.DoctorPhone = NullIfEmpty(dto.DoctorPhone);
            if (dto.EmergencyContactName != null)   student.EmergencyContactName = NullIfEmpty(dto.EmergencyContactName);
            if (dto.EmergencyContactPhone != null)  student.EmergencyContactPhone = NullIfEmpty(dto.EmergencyContactPhone);
            if (dto.EmergencyContactRelation != null) student.EmergencyContactRelation = NullIfEmpty(dto.EmergencyContactRelation);

            if (dto.FatherFullName != null)   student.FatherFullName = NullIfEmpty(dto.FatherFullName);
            if (dto.FatherPhone != null)      student.FatherPhone = NullIfEmpty(dto.FatherPhone);
            if (dto.FatherEmail != null)      student.FatherEmail = NullIfEmpty(dto.FatherEmail);
            if (dto.FatherProfession != null) student.FatherProfession = NullIfEmpty(dto.FatherProfession);
            if (dto.MotherFullName != null)   student.MotherFullName = NullIfEmpty(dto.MotherFullName);
            if (dto.MotherPhone != null)      student.MotherPhone = NullIfEmpty(dto.MotherPhone);
            if (dto.MotherEmail != null)      student.MotherEmail = NullIfEmpty(dto.MotherEmail);
            if (dto.MotherProfession != null) student.MotherProfession = NullIfEmpty(dto.MotherProfession);

            if (dto.Notes != null) student.Notes = NullIfEmpty(dto.Notes);

            // Tarif personnalisé : null = ne pas toucher, 0 = supprimer, > 0 = poser.
            var feeChanged = false;
            if (dto.MonthlyFeeFcfa.HasValue)
            {
                var existingFee = await _context.StudentFeeOverrides
                    .FirstOrDefaultAsync(o => o.StudentId == student.Id);

                if (dto.MonthlyFeeFcfa.Value <= 0)
                {
                    if (existingFee != null)
                    {
                        _context.StudentFeeOverrides.Remove(existingFee);
                        feeChanged = true;
                    }
                }
                else if (existingFee == null)
                {
                    _context.StudentFeeOverrides.Add(new StudentFeeOverride
                    {
                        StudentId = student.Id,
                        SchoolId = schoolId,
                        AmountFcfa = dto.MonthlyFeeFcfa.Value,
                        Reason = NullIfEmpty(dto.MonthlyFeeReason),
                        CreatedById = currentUserId,
                        CreatedAt = DateTime.UtcNow
                    });
                    feeChanged = true;
                }
                else
                {
                    feeChanged = existingFee.AmountFcfa != dto.MonthlyFeeFcfa.Value;
                    existingFee.AmountFcfa = dto.MonthlyFeeFcfa.Value;
                    if (dto.MonthlyFeeReason != null)
                        existingFee.Reason = NullIfEmpty(dto.MonthlyFeeReason);
                    existingFee.UpdatedAt = DateTime.UtcNow;
                }
            }

            if (!string.IsNullOrWhiteSpace(dto.PhotoBase64))
            {
                if (!string.IsNullOrEmpty(student.PhotoUrl))
                {
                    var oldPath = Path.Combine(_env.WebRootPath, student.PhotoUrl.TrimStart('/'));
                    if (File.Exists(oldPath))
                    {
                        try { File.Delete(oldPath); }
                        catch (Exception ex) { _logger.LogWarning(ex, "Impossible de supprimer l'ancienne photo {Path}", oldPath); }
                    }
                }
                student.PhotoUrl = await SavePhotoAsync(dto.PhotoBase64);
            }

            // Guardians : si null, on ne touche pas. Si fourni (même vide), on remplace.
            var newGuardians = new List<NewGuardianResult>();
            if (dto.Guardians != null)
            {
                var existingLinks = _context.StudentGuardians.Where(sg => sg.StudentId == student.Id);
                _context.StudentGuardians.RemoveRange(existingLinks);

                var school = await _context.Schools.FindAsync(schoolId);
                var schoolName = school?.Name ?? "Idara";

                foreach (var guardianDto in dto.Guardians)
                {
                    var (guardian, created) = await GetOrCreateGuardianAsync(guardianDto, schoolId, schoolName);
                    if (created != null) newGuardians.Add(created);
                    _context.StudentGuardians.Add(new StudentGuardian
                    {
                        StudentId = student.Id,
                        GuardianId = guardian.Id,
                        Relationship = guardianDto.Relationship,
                        IsPrimaryGuardian = guardianDto.IsPrimaryGuardian
                    });
                }
            }

            student.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // POST-ENREGISTREMENT : si l'un des trois leviers de tarif a bougé,
            // on réaligne les factures IMPAYÉES de cet élève sur son nouveau
            // montant — même règle que pour un changement de tarif côté école
            // (décision produit 2026-07-07). Sans cela, déplacer un élève d'une
            // classe à 10 000 vers une classe à 15 000 lui laisserait une facture
            // à 10 000 : l'écran et la facture raconteraient deux choses
            // différentes, sans erreur ni trace.
            // Les factures payées, partiellement payées, annulées ou visées par
            // un paiement en cours ne sont JAMAIS touchées (garanti par
            // InvoiceRepricingService, qui est aussi best-effort : un échec ne
            // doit pas annuler une modification de fiche déjà enregistrée).
            if (feeChanged
                || student.ClassId != previousClassId
                || student.BoardingStatus != previousBoarding)
            {
                await _repricing.RepriceUnpaidInvoicesAsync(
                    schoolId, new[] { student.Id }, CancellationToken.None);
            }

            // POST-COMMIT : envoi SMS de bienvenue best-effort des nouveaux responsables.
            foreach (var g in newGuardians)
                await _notif.SendSmsAsync(g.Sms);

            var updated = await GetStudentByIdAsync(student.Id, schoolId);
            if (updated != null)
                updated.NewGuardianCredentials = newGuardians.Select(g => g.Credential).ToList();
            return updated;
        }

        public async Task<bool> DeleteStudentAsync(int id, int schoolId)
        {
            var student = await _context.Students
                .FirstOrDefaultAsync(s => s.Id == id && s.SchoolId == schoolId && !s.IsDeleted);
            if (student == null) return false;

            student.IsDeleted = true;
            student.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<StudentDocumentResponseDto?> AddDocumentAsync(int studentId, int schoolId, StudentDocumentInputDto dto)
        {
            var student = await _context.Students
                .FirstOrDefaultAsync(s => s.Id == studentId && s.SchoolId == schoolId && !s.IsDeleted);
            if (student == null) return null;

            var saved = await SaveDocumentFileAsync(dto);
            if (saved == null) return null;

            var doc = new StudentDocument
            {
                StudentId = studentId,
                Type = dto.Type,
                FileName = saved.Value.fileName,
                OriginalFileName = dto.OriginalFileName,
                FilePath = saved.Value.filePath,
                ContentType = saved.Value.contentType,
                FileSize = saved.Value.size,
                UploadedAt = DateTime.UtcNow,
                Notes = dto.Notes
            };
            _context.StudentDocuments.Add(doc);
            await _context.SaveChangesAsync();

            return MapDocumentToDto(doc);
        }

        public async Task<bool> DeleteDocumentAsync(int studentId, int documentId, int schoolId)
        {
            var doc = await _context.StudentDocuments
                .Include(d => d.Student)
                .FirstOrDefaultAsync(d =>
                    d.Id == documentId &&
                    d.StudentId == studentId &&
                    d.Student.SchoolId == schoolId);
            if (doc == null) return false;

            // Suppression du fichier physique
            try
            {
                var fullPath = Path.Combine(_env.WebRootPath, doc.FilePath.TrimStart('/'));
                if (File.Exists(fullPath)) File.Delete(fullPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Impossible de supprimer le fichier {Path}", doc.FilePath);
            }

            _context.StudentDocuments.Remove(doc);
            await _context.SaveChangesAsync();
            return true;
        }

        // ----- Helpers : uploads -----

        /// <summary>
        /// Décode un base64 en validant taille et MIME. Retourne (bytes, extension, contentType)
        /// ou null si la donnée est invalide. La taille max et la liste blanche viennent de UploadSettings.
        /// </summary>
        private (byte[] bytes, string extension, string contentType)? DecodeAndValidateBase64(
            string? base64Input,
            int maxSizeMb,
            string[] allowedMimeTypes,
            string? declaredContentType = null,
            string? originalFileName = null)
        {
            if (string.IsNullOrWhiteSpace(base64Input)) return null;

            string? mimeFromHeader = null;
            string base64Data;

            // Format possible : "data:image/png;base64,iVBOR..." OU base64 brut.
            if (base64Input.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var commaIdx = base64Input.IndexOf(',');
                if (commaIdx <= 5) return null;
                var header = base64Input.Substring(5, commaIdx - 5); // ex "image/png;base64"
                var semi = header.IndexOf(';');
                mimeFromHeader = (semi > 0 ? header[..semi] : header).Trim().ToLowerInvariant();
                base64Data = base64Input[(commaIdx + 1)..];
            }
            else
            {
                base64Data = base64Input;
            }

            byte[] bytes;
            try { bytes = Convert.FromBase64String(base64Data); }
            catch (FormatException) { return null; }

            var maxBytes = (long)maxSizeMb * 1024 * 1024;
            if (bytes.LongLength == 0 || bytes.LongLength > maxBytes)
                return null;

            // Détermine le content-type effectif (priorité : header data-uri, puis déclaré, puis extension, puis magic-bytes).
            var ext = string.IsNullOrWhiteSpace(originalFileName) ? null : Path.GetExtension(originalFileName)?.ToLowerInvariant();
            var contentType = mimeFromHeader
                ?? (string.IsNullOrWhiteSpace(declaredContentType) ? null : declaredContentType.Trim().ToLowerInvariant())
                ?? ExtensionToMime(ext)
                ?? SniffMimeFromBytes(bytes)
                ?? "application/octet-stream";

            if (!allowedMimeTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
                return null;

            // Sanity check : comparer header avec magic-bytes pour bloquer les faux positifs simples.
            var sniffed = SniffMimeFromBytes(bytes);
            if (sniffed != null && !MimeMatchesFamily(contentType, sniffed))
                return null;

            // Extension finale (préfère celle du content-type).
            var finalExt = MimeToExtension(contentType) ?? ext ?? ".bin";
            return (bytes, finalExt, contentType);
        }

        private static string? ExtensionToMime(string? ext) => ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            _ => null
        };

        private static string? MimeToExtension(string mime) => mime.ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "application/pdf" => ".pdf",
            _ => null
        };

        private static bool MimeMatchesFamily(string declared, string sniffed)
        {
            declared = declared.ToLowerInvariant();
            sniffed = sniffed.ToLowerInvariant();
            if (declared == sniffed) return true;
            // jpeg/jpg équivalents
            if ((declared == "image/jpeg" || declared == "image/jpg") &&
                (sniffed == "image/jpeg" || sniffed == "image/jpg")) return true;
            return false;
        }

        /// <summary>Détecte JPEG/PNG/GIF/WebP/PDF par magic bytes.</summary>
        private static string? SniffMimeFromBytes(byte[] bytes)
        {
            if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
                return "image/jpeg";
            if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                return "image/png";
            if (bytes.Length >= 4 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38)
                return "image/gif";
            if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
                && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
                return "image/webp";
            if (bytes.Length >= 4 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46)
                return "application/pdf";
            return null;
        }

        private async Task<string?> SavePhotoAsync(string base64)
        {
            var decoded = DecodeAndValidateBase64(
                base64,
                _uploads.MaxPhotoSizeMb,
                _uploads.AllowedPhotoMimeTypes);
            if (decoded == null)
            {
                _logger.LogWarning("Photo rejetée (format invalide, MIME interdit ou taille > {Max} Mo).", _uploads.MaxPhotoSizeMb);
                throw new InvalidOperationException(
                    $"La photo est invalide (formats acceptés : JPEG/PNG/GIF/WEBP, max {_uploads.MaxPhotoSizeMb} Mo).");
            }

            var fileName = $"{Guid.NewGuid()}{decoded.Value.extension}";
            var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "students");
            Directory.CreateDirectory(uploadsFolder);

            var filePath = Path.Combine(uploadsFolder, fileName);
            await File.WriteAllBytesAsync(filePath, decoded.Value.bytes);

            return $"/uploads/students/{fileName}";
        }

        private async Task<(string fileName, string filePath, string contentType, long size)?> SaveDocumentFileAsync(StudentDocumentInputDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.ContentBase64)) return null;

            var decoded = DecodeAndValidateBase64(
                dto.ContentBase64,
                _uploads.MaxDocumentSizeMb,
                _uploads.AllowedDocumentMimeTypes,
                dto.ContentType,
                dto.OriginalFileName);
            if (decoded == null)
            {
                _logger.LogWarning("Document {Name} rejeté (MIME interdit ou taille > {Max} Mo).",
                    dto.OriginalFileName, _uploads.MaxDocumentSizeMb);
                throw new InvalidOperationException(
                    $"Le document '{dto.OriginalFileName}' est invalide (PDF/JPEG/PNG/WEBP, max {_uploads.MaxDocumentSizeMb} Mo).");
            }

            var fileName = $"{Guid.NewGuid()}{decoded.Value.extension}";
            var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "students", "docs");
            Directory.CreateDirectory(uploadsFolder);
            var fullPath = Path.Combine(uploadsFolder, fileName);
            await File.WriteAllBytesAsync(fullPath, decoded.Value.bytes);

            return (fileName, $"/uploads/students/docs/{fileName}", decoded.Value.contentType, decoded.Value.bytes.LongLength);
        }

        /// <summary>
        /// Récupère ou crée un Guardian pour l'école courante.
        /// — Si aucun Guardian n'existe avec cet email : on en crée un nouveau, lié à l'école via le StudentGuardian appelant.
        /// — Si un Guardian existe ET est déjà lié à un élève de notre école : on le réutilise (mise à jour des infos pour cette école).
        /// — Si un Guardian existe mais n'a aucun lien avec notre école : on le réutilise SANS écraser
        ///   ses données personnelles (FirstName/LastName/PhoneNumber) pour éviter qu'une école A
        ///   modifie les infos d'un parent connu d'une école B.
        /// </summary>
        /// <summary>Responsable nouvellement créé : son identifiant à afficher
        /// + la requête SMS à envoyer APRÈS le commit (best-effort).</summary>
        private sealed record NewGuardianResult(
            DTOs.Common.UserCredentialDto Credential, NotificationSmsRequest Sms);

        private async Task<(User user, NewGuardianResult? created)> GetOrCreateGuardianAsync(
            GuardianInputDto dto,
            int schoolId,
            string schoolName,
            string adminLanguage = "fr")
        {
            // Identité = TÉLÉPHONE (incrément 2). Email facultatif (normalisé minuscules).
            var phone = SenegalPhone.Normalize(dto.PhoneNumber);
            if (phone == null)
                throw new InvalidOperationException(
                    $"Numéro de téléphone du responsable invalide : '{dto.PhoneNumber}'.");
            var email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim().ToLowerInvariant();

            // OrderBy(Id) : lookup déterministe (le numéro sert d'identité de login).
            var existing = await _context.Users
                .Where(u => u.PhoneNumber == phone
                            && u.Role == UserRoles.Guardian
                            && !u.IsDeleted)
                .OrderBy(u => u.Id)
                .FirstOrDefaultAsync();

            if (existing != null)
            {
                var alreadyLinkedToOurSchool = await _context.StudentGuardians
                    .AnyAsync(sg => sg.GuardianId == existing.Id
                                    && sg.Student.SchoolId == schoolId
                                    && !sg.Student.IsDeleted);

                if (alreadyLinkedToOurSchool)
                {
                    // Notre école connaît déjà ce parent : on accepte la mise à jour
                    // (mais on ne touche PAS au mot de passe ni au numéro = identité).
                    existing.FirstName = dto.FirstName;
                    existing.LastName = dto.LastName;
                    existing.FullName = dto.ComposeFullName();
                    if (email != null) existing.Email = email;
                    await _context.SaveChangesAsync();
                }
                // sinon : on ne touche pas aux données personnelles existantes (anti cross-tenant).
                // Responsable déjà existant : pas de nouveau code à communiquer.
                return (existing, null);
            }

            // Le numéro est une identité UNIQUE de login. Aucun Guardian ne l'a
            // (existing == null) ; s'il appartient à un AUTRE compte (enseignant/
            // personnel), on refuse de créer un doublon ambigu (sinon login non
            // déterministe) plutôt que de casser silencieusement l'auth.
            var phoneTakenByOther = await _context.Users
                .AnyAsync(u => u.PhoneNumber == phone && !u.IsDeleted);
            if (phoneTakenByOther)
                throw new InvalidOperationException(
                    $"Le numéro {phone} est déjà utilisé par un autre compte. Utilisez un autre numéro pour ce responsable.");

            // Nouveau responsable : code à 6 chiffres = mot de passe initial.
            var code = SixDigitCode();
            var newGuardian = new User
            {
                Email = email,
                FirstName = dto.FirstName,
                LastName = dto.LastName,
                FullName = dto.ComposeFullName(),
                PhoneNumber = phone,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(code),
                Role = UserRoles.Guardian,
                IsEmailVerified = true,
                AccountStatus = AccountStatus.Active,
                SchoolId = null,
                CreatedAt = DateTime.UtcNow,
                // Hérite de la langue de l'admin qui ajoute (env. culturellement homogène).
                PreferredLanguage = adminLanguage,
            };

            _context.Users.Add(newGuardian);
            await _context.SaveChangesAsync();

            // Message de bienvenue (numéro + code). ⚠️ L'ENVOI SMS est fait en
            // POST-COMMIT par l'appelant (CreateStudent/UpdateStudent) : on ne
            // doit pas envoyer un SMS si la transaction est ensuite rollback
            // (cf. §42/§57). Ici on prépare juste le message + le code.
            var platform = await _context.GetPlatformSettingsAsync();
            var displayName = dto.ComposeFullName();
            var welcome = NotificationTemplates.InviteWelcome(
                // Un responsable connu sous un seul nom n'a pas forcément de
                // prénom : on retombe sur le nom composé plutôt que d'ouvrir le
                // message par un vide (« Bonjour , … »).
                string.IsNullOrWhiteSpace(dto.FirstName) ? displayName : dto.FirstName,
                schoolName, "Responsable", "ولي الأمر", phone, code);
            // Message PARTAGÉ manuellement (modal récap) = FR simple ; le SMS auto
            // garde le template bilingue `welcome`.
            var messageText = NotificationTemplates.CredentialShare(
                displayName, phone, code);

            var credential = new DTOs.Common.UserCredentialDto
            {
                FullName = displayName,
                Phone = phone,
                Code = code,
                Message = messageText,
            };
            var sms = new NotificationSmsRequest(
                UserId: newGuardian.Id,
                RawPhone: phone,
                PreferredLanguage: adminLanguage,
                Message: welcome,
                Bilingual: platform.SmsBilingual,
                TemplateCode: "INVITE",
                RelatedEntityId: newGuardian.Id);
            return (newGuardian, new NewGuardianResult(credential, sms));
        }

        private static string SixDigitCode() =>
            System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

        private static string? NullIfEmpty(string? s) =>
            string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static StudentResponseDto MapToResponseDto(
            Student student, StudentFeeOverride? fee = null) => new()
        {
            BoardingStatus = student.BoardingStatus,
            MonthlyFeeFcfa = fee?.AmountFcfa,
            MonthlyFeeReason = fee?.Reason,

            Id = student.Id,
            FirstName = student.FirstName,
            LastName = student.LastName,
            MiddleName = student.MiddleName,
            Gender = student.Gender,
            DateOfBirth = student.DateOfBirth,
            PlaceOfBirth = student.PlaceOfBirth,
            Nationality = student.Nationality,
            PhotoUrl = student.PhotoUrl,

            Address = student.Address,
            City = student.City,
            Region = student.Region,
            Country = student.Country,

            EnrollmentDate = student.EnrollmentDate,
            ClassId = student.ClassId,
            ClassName = student.Class?.Name,
            StudentNumber = student.StudentNumber,
            PreviousSchool = student.PreviousSchool,
            PreviousClass = student.PreviousClass,
            TransferReason = student.TransferReason,

            BloodType = student.BloodType,
            Allergies = student.Allergies,
            ChronicConditions = student.ChronicConditions,
            CurrentMedications = student.CurrentMedications,
            DoctorName = student.DoctorName,
            DoctorPhone = student.DoctorPhone,
            EmergencyContactName = student.EmergencyContactName,
            EmergencyContactPhone = student.EmergencyContactPhone,
            EmergencyContactRelation = student.EmergencyContactRelation,

            FatherFullName = student.FatherFullName,
            FatherPhone = student.FatherPhone,
            FatherEmail = student.FatherEmail,
            FatherProfession = student.FatherProfession,
            MotherFullName = student.MotherFullName,
            MotherPhone = student.MotherPhone,
            MotherEmail = student.MotherEmail,
            MotherProfession = student.MotherProfession,

            Notes = student.Notes,

            CreatedAt = student.CreatedAt,
            UpdatedAt = student.UpdatedAt,

            Guardians = student.StudentGuardians?.Select(sg => new GuardianResponseDto
            {
                Id = sg.Guardian.Id,
                Email = sg.Guardian.Email,
                FullName = sg.Guardian.FullName,
                PhoneNumber = sg.Guardian.PhoneNumber,
                Relationship = sg.Relationship,
                IsPrimaryGuardian = sg.IsPrimaryGuardian
            }).ToList() ?? new List<GuardianResponseDto>(),

            Documents = student.Documents?.Select(MapDocumentToDto).ToList() ?? new List<StudentDocumentResponseDto>()
        };

        private static StudentDocumentResponseDto MapDocumentToDto(StudentDocument d) => new()
        {
            Id = d.Id,
            Type = d.Type,
            OriginalFileName = d.OriginalFileName,
            FilePath = d.FilePath,
            ContentType = d.ContentType,
            FileSize = d.FileSize,
            UploadedAt = d.UploadedAt,
            Notes = d.Notes
        };
    }
}
