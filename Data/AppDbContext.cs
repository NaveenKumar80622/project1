using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Models;
using PickNBook.Api.Models.Entities;
using PickNBook.Api.Models.Payments;

namespace PickNBook.Api.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        // =============================
        // DbSets
        // =============================
        public DbSet<User> Users { get; set; }
        public DbSet<PickNBook.Api.Models.OTP> OTPs { get; set; }
        public DbSet<PickNBook.Api.Models.UserLockout> UserLockouts { get; set; }
        // public DbSet<OTP> OTPs { get; set; } // Replaced by OtpRecords
        public DbSet<OtpRecord> OtpRecords => Set<OtpRecord>();
        public DbSet<FeaturedOffer> FeaturedOffers { get; set; }
        public DbSet<CouponRedemption> CouponRedemptions { get; set; }
        public DbSet<OfferSubscriber> OfferSubscribers { get; set; }
        public DbSet<BlogPost> BlogPosts { get; set; }
        public DbSet<BlogCategory> BlogCategories { get; set; }
        public DbSet<BlogSubCategory> BlogSubCategories { get; set; }
        public DbSet<CmsPage> CmsPages { get; set; }
        public DbSet<AboutUs> AboutUs { get; set; }
        public DbSet<AboutUsCount> AboutUsCounts { get; set; }
        public DbSet<AboutUsTeamMember> AboutUsTeamMembers { get; set; }
        public DbSet<MenuItem> MenuItems { get; set; }
        public DbSet<DepositRequest> DepositRequests { get; set; }
        public DbSet<ContactQuery> ContactQueries { get; set; }
        public DbSet<BusBooking> BusBookings => Set<BusBooking>();
        public DbSet<FlightReservation> FlightReservations => Set<FlightReservation>();
        public DbSet<BusReservation> BusReservations => Set<BusReservation>();
        public DbSet<BusReservationPassenger> BusReservationPassengers => Set<BusReservationPassenger>();
        public DbSet<FlightReservationPassenger> FlightReservationPassengers => Set<FlightReservationPassenger>();
        public DbSet<Traveler> Travelers => Set<Traveler>();
        public DbSet<FlightRouteStat> FlightRouteStats => Set<FlightRouteStat>();
        public DbSet<BusRouteStat> BusRouteStats => Set<BusRouteStat>();
        public DbSet<PlaceSearchStat> PlaceSearchStats => Set<PlaceSearchStat>();
        public DbSet<BusDiscount> BusDiscounts => Set<BusDiscount>();
        public DbSet<BusCoupon> BusCoupons => Set<BusCoupon>();
        public DbSet<BusCouponUsage> BusCouponUsages => Set<BusCouponUsage>();
        public DbSet<BusConvenienceFee> BusConvenienceFees => Set<BusConvenienceFee>();
        public DbSet<BusSearchLog> BusSearchLogs => Set<BusSearchLog>();
        public DbSet<BusMarkupSetting> BusMarkupSettings => Set<BusMarkupSetting>();

        public DbSet<BusGstSetting> BusGstSettings => Set<BusGstSetting>();
        public DbSet<FlightDiscount> FlightDiscounts => Set<FlightDiscount>();
        public DbSet<FlightRemark> FlightRemarks => Set<FlightRemark>();
        public DbSet<FlightCoupon> FlightCoupons => Set<FlightCoupon>();
        public DbSet<FlightCouponUsage> FlightCouponUsages => Set<FlightCouponUsage>();
        public DbSet<FlightConvenienceFee> FlightConvenienceFees => Set<FlightConvenienceFee>();
        public DbSet<FlightSearchLog> FlightSearchLogs => Set<FlightSearchLog>();
        public DbSet<PendingAirline> PendingAirlines => Set<PendingAirline>();
        public DbSet<Airline> Airlines => Set<Airline>();
        public DbSet<AirlineWebcheckLink> AirlineWebcheckLinks => Set<AirlineWebcheckLink>();
        public DbSet<PopularDestination> PopularDestinations => Set<PopularDestination>();
        public DbSet<FlightCancellationRequest> FlightCancellationRequests => Set<FlightCancellationRequest>();
        public DbSet<FlightAmendmentRequest> FlightAmendmentRequests => Set<FlightAmendmentRequest>();
        public DbSet<BusPromotion> BusPromotions => Set<BusPromotion>();
        public DbSet<HotelReservation> HotelReservations => Set<HotelReservation>();
        public DbSet<HotelBlockedPrice> HotelBlockedPrices => Set<HotelBlockedPrice>();
        public DbSet<BusBookingSummary> BusBookingSummaries => Set<BusBookingSummary>();
        
        public DbSet<BusBlockedSeatPrice> BusBlockedSeatPrices => Set<BusBlockedSeatPrice>();

        public DbSet<BusPromotionCondition> BusPromotionConditions => Set<BusPromotionCondition>();

        public DbSet<BusPromotionUsage> BusPromotionUsages => Set<BusPromotionUsage>();

        public DbSet<FeaturedOfferCondition> FeaturedOfferConditions => Set<FeaturedOfferCondition>();

        public DbSet<FeaturedOfferUsage> FeaturedOfferUsages => Set<FeaturedOfferUsage>();

        public DbSet<BusDiscountCondition> BusDiscountConditions => Set<BusDiscountCondition>();

        public DbSet<FlightPromotion> FlightPromotions => Set<FlightPromotion>();
        public DbSet<FlightPromotionCondition> FlightPromotionConditions => Set<FlightPromotionCondition>();
        public DbSet<FlightPromotionUsage> FlightPromotionUsages => Set<FlightPromotionUsage>();
        public DbSet<FlightMarkupRule> FlightMarkupRules => Set<FlightMarkupRule>();
        public DbSet<FlightConvenienceFeeRule> FlightConvenienceFeeRules => Set<FlightConvenienceFeeRule>();
        public DbSet<FlightDiscountCondition> FlightDiscountConditions => Set<FlightDiscountCondition>();
        public DbSet<HotelPricingSetting> HotelPricingSettings => Set<HotelPricingSetting>();
        public DbSet<Testimonial> Testimonials => Set<Testimonial>();
        public DbSet<TestimonialCategory> TestimonialCategories => Set<TestimonialCategory>();
        public DbSet<HotelCoupon> HotelCoupons => Set<HotelCoupon>();
        public DbSet<HotelCouponUsage> HotelCouponUsages => Set<HotelCouponUsage>();
        public DbSet<HotelSearchLog> HotelSearchLogs => Set<HotelSearchLog>();
        public DbSet<HotelInfoCache> HotelInfoCaches => Set<HotelInfoCache>();
        public DbSet<HotelMarkupRule> HotelMarkupRules => Set<HotelMarkupRule>();

        public DbSet<Theme> Themes => Set<Theme>();
        public DbSet<ThemeConfig> ThemeConfigs => Set<ThemeConfig>();
        public DbSet<AgentMarkupSetting> AgentMarkupSettings => Set<AgentMarkupSetting>();
        public DbSet<AgentLedgerEntry> AgentLedgerEntries => Set<AgentLedgerEntry>();
        public DbSet<B2BCommissionRule> B2BCommissionRules => Set<B2BCommissionRule>();

        // Security Management Module DbSets
        public DbSet<SecuritySettings> SecuritySettings => Set<SecuritySettings>();
        public DbSet<IpAccessRule> IpAccessRules => Set<IpAccessRule>();
        public DbSet<SecurityAuditLog> SecurityAuditLogs => Set<SecurityAuditLog>();
        public DbSet<SystemAppLock> SystemAppLocks => Set<SystemAppLock>();
        public DbSet<UserSession> UserSessions => Set<UserSession>();
        public DbSet<AdminSession> AdminSessions => Set<AdminSession>();
        public DbSet<SecurityNotificationMapping> SecurityNotificationMappings => Set<SecurityNotificationMapping>();
        public DbSet<SecurityNotification> SecurityNotifications => Set<SecurityNotification>();

        public DbSet<SecurityIpRule> SecurityIpRules => Set<SecurityIpRule>();
        public DbSet<SecurityLimit> SecurityLimits => Set<SecurityLimit>();
        public DbSet<SecurityAuthSetting> SecurityAuthSettings => Set<SecurityAuthSetting>();
        public DbSet<SecurityCounter> SecurityCounters => Set<SecurityCounter>();
        public DbSet<SecurityAccountLock> SecurityAccountLocks => Set<SecurityAccountLock>();
        public DbSet<SecurityApiRule> SecurityApiRules => Set<SecurityApiRule>();
        public DbSet<SecurityB2bWalletConfig> SecurityB2bWalletConfigs => Set<SecurityB2bWalletConfig>();

        // Email Management System DbSets
        public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();
        public DbSet<EmailReminder> EmailReminders => Set<EmailReminder>();
        public DbSet<EmailHistory> EmailHistory => Set<EmailHistory>();

        // Cashfree Payments DbSets
        public DbSet<Payment> Payments => Set<Payment>();
        public DbSet<PendingPaymentBooking> PendingPaymentBookings => Set<PendingPaymentBooking>();
        public DbSet<SupplierFulfillmentExecution> SupplierFulfillmentExecutions => Set<SupplierFulfillmentExecution>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // =============================
            // CASHFREE PAYMENTS CONFIG
            // =============================
            modelBuilder.Entity<Payment>(entity =>
            {
                entity.ToTable("payments");
                entity.Property(x => x.OriginalAmount).HasPrecision(18, 2);
                entity.Property(x => x.MarkupAmount).HasPrecision(18, 2);
                entity.Property(x => x.ConvenienceFee).HasPrecision(18, 2);
                entity.Property(x => x.DiscountAmount).HasPrecision(18, 2);
                entity.Property(x => x.FinalPayableAmount).HasPrecision(18, 2);
                entity.Property(x => x.Status).HasMaxLength(20);
                entity.Property(x => x.BookingType).HasMaxLength(20);
                entity.Property(x => x.Currency).HasMaxLength(10).HasDefaultValue("INR");
                entity.Property(x => x.PaymentReference).HasMaxLength(100);
                entity.Property(x => x.CashfreeOrderId).HasMaxLength(100);
                entity.Property(x => x.CashfreeCfOrderId).HasMaxLength(100);
                entity.Property(x => x.PaymentSessionId).HasMaxLength(500);
                entity.Property(x => x.CashfreePaymentId).HasMaxLength(100);
                entity.Property(x => x.PaymentMethod).HasMaxLength(50);
                entity.Property(x => x.CouponCode).HasMaxLength(50);
                entity.Property(x => x.OfferCode).HasMaxLength(50);

                entity.HasIndex(x => x.CashfreeOrderId).IsUnique();
                entity.HasIndex(x => x.CashfreeCfOrderId);
                entity.HasIndex(x => x.PaymentReference);
                entity.HasIndex(x => x.UserId);
                entity.HasIndex(x => x.Status);
                entity.HasIndex(x => x.FulfillmentStatus);
                entity.HasIndex(x => x.RefundStatus);
            });

            modelBuilder.Entity<PendingPaymentBooking>(entity =>
            {
                entity.ToTable("pending_payment_bookings");
                entity.Property(x => x.Amount).HasPrecision(18, 2);
                entity.Property(x => x.Status).HasMaxLength(20);
                entity.Property(x => x.BookingType).HasMaxLength(20);
                entity.Property(x => x.Currency).HasMaxLength(10).HasDefaultValue("INR");

                entity.HasOne<Payment>()
                      .WithMany()
                      .HasForeignKey(x => x.PaymentId)
                      .OnDelete(DeleteBehavior.Cascade);
                entity.HasIndex(x => x.PaymentId);
            });

            modelBuilder.Entity<PickNBook.Api.Models.Entities.SupplierFulfillmentExecution>(entity =>
            {
                entity.ToTable("supplier_fulfillment_executions");
                entity.HasIndex(x => x.PaymentId).IsUnique();
                
                entity.HasOne(x => x.Payment)
                      .WithOne()
                      .HasForeignKey<PickNBook.Api.Models.Entities.SupplierFulfillmentExecution>(x => x.PaymentId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // =============================
            // SECURITY SEED DATA
            // =============================
            var ruleKeys = new[] {
                "LOGIN_FAILURE", "INVALID_PASSWORD", "PASSWORD_MISMATCH",
                "OTP_GENERATION", "OTP_RESEND", "OTP_MISMATCH", "OTP_VERIFICATION",
                "REGISTRATION_ATTEMPT", "CREATED_ACCOUNT_PER_IP",
                "FORGOT_PASSWORD_REQUEST", "PASSWORD_RESET_ATTEMPT",
                "DAILY_LOGIN", "API_RATE_LIMIT", "OTP_EXPIRY", "OTP_COOLDOWN"
            };
            var scopes = new[] { "ADMIN", "USER", "B2B" };
            var limits = new System.Collections.Generic.List<SecurityLimit>();
            long idCounter = 1;
            foreach(var scope in scopes)
            {
                foreach(var rule in ruleKeys)
                {
                    limits.Add(new SecurityLimit {
                        Id = idCounter++,
                        Scope = scope,
                        RuleKey = rule,
                        RuleName = $"{rule.Replace("_", " ")} Limit",
                        IsEnabled = true,
                        LimitValue = 5,
                        TimePeriodValue = 10,
                        TimePeriodUnit = "MINUTES",
                        AccountAction = "NONE",
                        IpAction = "NONE",
                        BlockDurationValue = 60,
                        BlockDurationUnit = "MINUTES",
                        EmailEnabled = true,
                        ResetPeriodValue = 10,
                        ResetPeriodUnit = "MINUTES",
                        CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    });
                }
            }
            modelBuilder.Entity<SecurityLimit>().HasData(limits);

            modelBuilder.Entity<SecurityLimit>()
                .HasIndex(s => new { s.Scope, s.RuleKey })
                .IsUnique();

            modelBuilder.Entity<SecurityAuthSetting>()
                .HasIndex(s => new { s.Scope, s.Category })
                .IsUnique();



            modelBuilder.Entity<BusBookingSummary>(entity =>
            {
                entity.HasNoKey();
                entity.ToView("v_BusBookingSummary");
            });

            modelBuilder.Entity<BusPromotion>()
                .ToTable("buspromotions");

            modelBuilder.Entity<BusPromotionCondition>()
                .ToTable("buspromotionconditions");

            modelBuilder.Entity<BusPromotionUsage>()
                .ToTable("buspromotionusages");

            modelBuilder.Entity<BusDiscountCondition>()
                .ToTable("busdiscountconditions");

            modelBuilder.Entity<FlightDiscountCondition>()
                .ToTable("flightdiscountconditions");

            // =============================
            // TABLE NAME MAPPING
            // =============================
            modelBuilder.Entity<User>().ToTable("users");
            modelBuilder.Entity<OTP>().ToTable("otps");
            modelBuilder.Entity<FeaturedOffer>().ToTable("featuredoffers");
            modelBuilder.Entity<CouponRedemption>().ToTable("couponredemptions");
            modelBuilder.Entity<OfferSubscriber>().ToTable("offersubscribers");
            modelBuilder.Entity<BlogPost>().ToTable("blogposts");
            modelBuilder.Entity<BlogCategory>().ToTable("blogcategories");
            modelBuilder.Entity<BlogSubCategory>().ToTable("blogsubcategories");
            modelBuilder.Entity<CmsPage>().ToTable("cmspages");
            modelBuilder.Entity<AboutUs>().ToTable("about_us");
            modelBuilder.Entity<AboutUsCount>().ToTable("about_us_counts");
            modelBuilder.Entity<AboutUsTeamMember>().ToTable("about_us_team_members");
            modelBuilder.Entity<ContactQuery>().ToTable("contact_queries");
            modelBuilder.Entity<Testimonial>().ToTable("testimonials");
            modelBuilder.Entity<TestimonialCategory>().ToTable("testimonialcategories");

            modelBuilder.Entity<PlaceSearchStat>(entity =>
            {
                entity.ToTable("place_search_stats");
                entity.HasIndex(e => new { e.CityName, e.TripType })
                      .IsUnique()
                      .HasDatabaseName("IX_place_search_stats_city_name_trip_type");
            });

            modelBuilder.Entity<Testimonial>()
                .HasOne(t => t.Category)
                .WithMany()
                .HasForeignKey(t => t.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<HotelCoupon>().ToTable("hotel_coupons");
            modelBuilder.Entity<HotelCouponUsage>().ToTable("hotel_coupon_usages");

            modelBuilder.Entity<HotelCoupon>()
                .HasIndex(x => x.CouponCode)
                .IsUnique();

            modelBuilder.Entity<HotelCouponUsage>()
                .HasOne(x => x.HotelReservation)
                .WithMany()
                .HasForeignKey(x => x.HotelReservationId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<HotelSearchLog>().ToTable("hotel_search_logs");
            modelBuilder.Entity<HotelInfoCache>().ToTable("hotel_info_caches");

            modelBuilder.Entity<HotelMarkupRule>(entity =>
            {
                entity.ToTable("hotel_markup_rules");
                entity.Property(x => x.RuleName).HasMaxLength(150).IsRequired();
                entity.Property(x => x.CityCode).HasMaxLength(50).IsRequired();
                entity.Property(x => x.HotelCode).HasMaxLength(100).IsRequired();
                entity.Property(x => x.UserType).HasMaxLength(20).IsRequired();
                entity.Property(x => x.MarkupType).HasMaxLength(20).IsRequired();
                entity.Property(x => x.MarkupValue).HasPrecision(12, 2).IsRequired();
            });

            modelBuilder.Entity<HotelReservation>(entity =>
            {
                entity.ToTable("hotel_reservations");
                entity.Property(x => x.SrdvOfferedPrice).HasPrecision(18, 2);
                entity.Property(x => x.SrdvGstAmount).HasPrecision(18, 2);
                entity.Property(x => x.SrdvCgstAmount).HasPrecision(18, 2);
                entity.Property(x => x.SrdvSgstAmount).HasPrecision(18, 2);
                entity.Property(x => x.SrdvIgstAmount).HasPrecision(18, 2);
                entity.Property(x => x.ConfirmationNo).HasMaxLength(100);
                entity.Property(x => x.InvoiceNumber).HasMaxLength(100);
                entity.Property(x => x.RatePlanCode).HasMaxLength(100);
                entity.Property(x => x.RoomTypeCode).HasMaxLength(150);
            });

            // =============================
            // User Configuration
            // =============================
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            modelBuilder.Entity<User>()
                .Property(u => u.Role)
                .HasMaxLength(20)
                .HasDefaultValue(AuthRoles.User);

            modelBuilder.Entity<User>()
                .HasIndex(u => u.Role);

            modelBuilder.Entity<User>()
                .Property(u => u.WalletBalance)
                .HasPrecision(18, 2);

            modelBuilder.Entity<User>()
                .Property(u => u.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Active");

            modelBuilder.Entity<User>()
                .Property(u => u.WalletStatus)
                .HasMaxLength(20)
                .HasDefaultValue("Active");

            modelBuilder.Entity<User>()
                .Property(u => u.Gender)
                .HasMaxLength(10)
                .HasDefaultValue("Male");

            modelBuilder.Entity<User>()
                .Property(u => u.Currency)
                .HasMaxLength(10)
                .HasDefaultValue("INR");

            modelBuilder.Entity<User>()
                .Property(u => u.LoginId)
                .HasMaxLength(150);

            // =============================
            // OTP Configuration
            // =============================
            modelBuilder.Entity<OTP>()
                .HasOne(o => o.User)
                .WithMany()
                .HasForeignKey(o => o.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<OTP>()
                .Property(o => o.Purpose)
                .HasMaxLength(50);

            modelBuilder.Entity<OTP>()
                .Property(o => o.ChallengeId)
                .HasMaxLength(64);

            modelBuilder.Entity<OTP>()
                .HasIndex(o => new { o.UserId, o.Purpose, o.IsUsed, o.Expiry });

            modelBuilder.Entity<OTP>()
                .HasIndex(o => o.ChallengeId);

            // =============================
            // FeaturedOffer Configuration
            // =============================
            modelBuilder.Entity<FeaturedOffer>(entity =>
            {
                entity.Property(x => x.DiscountValue).HasPrecision(10, 2);
                entity.Property(x => x.MaxDiscountAmount).HasPrecision(10, 2);
                entity.Property(x => x.MinBookingAmount).HasPrecision(10, 2);

                entity.HasMany(x => x.Usages)
                    .WithOne(x => x.FeaturedOffer)
                    .HasForeignKey(x => x.FeaturedOfferId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // =============================
            // FeaturedOfferUsage Configuration
            // =============================
            modelBuilder.Entity<FeaturedOfferUsage>(entity =>
            {
                entity.ToTable("featuredofferusages");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.UserId).HasMaxLength(50).IsRequired();
                entity.Property(x => x.DiscountAmount).HasPrecision(10, 2);

                entity.HasOne(x => x.BusReservation)
                    .WithMany()
                    .HasForeignKey(x => x.BusReservationId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // =============================
            // FeaturedOfferCondition Configuration
            // =============================
            modelBuilder.Entity<FeaturedOfferCondition>(entity =>
            {
                entity.ToTable("featuredofferconditions");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.ConditionType).HasMaxLength(50).IsRequired();
                entity.Property(x => x.Value1).HasMaxLength(200).IsRequired();
                entity.Property(x => x.Value2).HasMaxLength(200);
                entity.HasOne(x => x.FeaturedOffer)
                    .WithMany(x => x.Conditions)
                    .HasForeignKey(x => x.FeaturedOfferId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // =============================
            // BusPromotionUsage Configuration
            // =============================
            modelBuilder.Entity<BusPromotionUsage>(entity =>
            {
                entity.ToTable("buspromotionusages");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.UserId).HasMaxLength(50).IsRequired();
                entity.Property(x => x.PromotionCode).HasMaxLength(40).IsRequired();
                entity.Property(x => x.PromotionType).HasMaxLength(20).IsRequired();
                entity.Property(x => x.BookingStatus).HasMaxLength(20).IsRequired();
                entity.Property(x => x.DiscountAmountInr).HasPrecision(10, 2);
                entity.Property(x => x.BookingTotalInr).HasPrecision(10, 2);

                entity.HasOne(x => x.Promotion)
                    .WithMany()
                    .HasForeignKey(x => x.BusPromotionId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.BusReservation)
                    .WithMany()
                    .HasForeignKey(x => x.BusReservationId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // =============================
            // CouponRedemption Configuration
            // =============================
            modelBuilder.Entity<CouponRedemption>()
                .Property(x => x.OriginalPrice)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CouponRedemption>()
                .Property(x => x.DiscountAmount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CouponRedemption>()
                .Property(x => x.FinalPrice)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CouponRedemption>().Property(x => x.RedeemedAtUtc);

            modelBuilder.Entity<CouponRedemption>()
                .HasIndex(x => new { x.OfferCode, x.CouponCode, x.RedeemedAtUtc });

            modelBuilder.Entity<CouponRedemption>()
                .HasOne(x => x.FeaturedOffer)
                .WithMany()
                .HasForeignKey(x => x.FeaturedOfferId)
                .OnDelete(DeleteBehavior.Cascade);

            // =============================
            // OfferSubscriber Configuration
            // =============================
            modelBuilder.Entity<OfferSubscriber>()
                .HasIndex(x => x.Email)
                .IsUnique();

            modelBuilder.Entity<OfferSubscriber>().Property(x => x.SubscribedAtUtc);
            modelBuilder.Entity<OfferSubscriber>().Property(x => x.UpdatedAtUtc);

            // =============================
            // BlogPost Configuration
            // =============================
            modelBuilder.Entity<BlogPost>()
                .HasIndex(x => x.Slug)
                .IsUnique();

            modelBuilder.Entity<BlogPost>()
                .HasIndex(x => new { x.IsPublished, x.PublishedAtUtc });

            modelBuilder.Entity<BlogPost>()
                .HasIndex(x => x.Category);

            // =============================
            // BlogCategory Configuration
            // =============================
            modelBuilder.Entity<BlogCategory>()
                .HasIndex(x => x.Slug)
                .IsUnique();

            modelBuilder.Entity<BlogCategory>()
                .Property(x => x.Name)
                .HasMaxLength(150)
                .IsRequired();

            modelBuilder.Entity<BlogCategory>()
                .Property(x => x.Slug)
                .HasMaxLength(180)
                .IsRequired();

            modelBuilder.Entity<BlogCategory>()
                .Property(x => x.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Active");

            // =============================
            // BlogSubCategory Configuration
            // =============================
            modelBuilder.Entity<BlogSubCategory>()
                .HasIndex(x => x.Slug)
                .IsUnique();

            modelBuilder.Entity<BlogSubCategory>()
                .Property(x => x.Name)
                .HasMaxLength(150)
                .IsRequired();

            modelBuilder.Entity<BlogSubCategory>()
                .Property(x => x.Category)
                .HasMaxLength(150)
                .IsRequired();

            modelBuilder.Entity<BlogSubCategory>()
                .Property(x => x.Slug)
                .HasMaxLength(180)
                .IsRequired();

            modelBuilder.Entity<BlogSubCategory>()
                .Property(x => x.Status)
                .HasMaxLength(20)
                .HasDefaultValue("Active");

            modelBuilder.Entity<BlogPost>()
                .Property(x => x.Title)
                .HasMaxLength(200);

            modelBuilder.Entity<BlogPost>()
                .Property(x => x.Slug)
                .HasMaxLength(220);

            modelBuilder.Entity<BlogPost>()
                .Property(x => x.Category)
                .HasMaxLength(80);

            modelBuilder.Entity<BlogPost>()
                .Property(x => x.SubCategory)
                .HasMaxLength(80);

            modelBuilder.Entity<BlogPost>()
                .Property(x => x.SubTitle)
                .HasMaxLength(200);

            modelBuilder.Entity<BlogPost>()
                .Property(x => x.MetaTitle)
                .HasMaxLength(200);

            modelBuilder.Entity<BlogPost>()
                .Property(x => x.MetaKeyword)
                .HasMaxLength(300);

            modelBuilder.Entity<BlogPost>()
                .Property(x => x.MetaDescription)
                .HasMaxLength(600);

            modelBuilder.Entity<BlogPost>()
                .Property(x => x.ImageUrl)
                .HasMaxLength(300);

            modelBuilder.Entity<BlogPost>()
                .Property(x => x.OgImageUrl)
                .HasMaxLength(300);

            modelBuilder.Entity<BlogPost>()
                .Property(x => x.AddedByName)
                .HasMaxLength(120);

            modelBuilder.Entity<BlogPost>().Property(x => x.CreatedAtUtc);
            modelBuilder.Entity<BlogPost>().Property(x => x.UpdatedAtUtc);
            modelBuilder.Entity<BlogPost>().Property(x => x.PublishedAtUtc);

            // =============================
            // CmsPage Configuration
            // =============================
            modelBuilder.Entity<CmsPage>(entity =>
            {
                entity.HasIndex(x => x.Slug).IsUnique();
                entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
                entity.Property(x => x.Slug).HasMaxLength(220).IsRequired();
                entity.Property(x => x.Module).HasMaxLength(80).HasDefaultValue("All");
                entity.Property(x => x.Status).HasMaxLength(20).HasDefaultValue("Active");
                entity.Property(x => x.MetaTitle).HasMaxLength(200);
                entity.Property(x => x.MetaKeyword).HasMaxLength(300);
                entity.Property(x => x.MetaDescription).HasMaxLength(600);
                entity.Property(x => x.ImageUrl).HasMaxLength(300);
                entity.Property(x => x.BannerUrl).HasMaxLength(300);
                entity.Property(x => x.CreatedAtUtc).IsRequired();
                entity.Property(x => x.UpdatedAtUtc).IsRequired();
            });

            // =============================
            // AboutUs Configuration & Seeding
            // =============================
            modelBuilder.Entity<AboutUs>(entity =>
            {
                entity.HasIndex(x => x.Module).IsUnique();
                entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
                entity.Property(x => x.Module).HasMaxLength(20).IsRequired();
                entity.Property(x => x.WhoWeAreHeading).HasMaxLength(500).IsRequired();
                entity.Property(x => x.WhoWeAreImageUrl).HasMaxLength(1000);

                entity.HasData(new AboutUs
                {
                    Id = 1,
                    AboutDescription = "<p>Pick N Book is a leading travel booking provider delivering flights and bus bookings to travelers worldwide.</p>",
                    Status = "active",
                    Module = "B2C",
                    WhoWeAreHeading = "Who We Are",
                    WhoWeAreDescription = "<p>We are a dedicated team of travel enthusiasts and product engineers building seamless transport bookings.</p>",
                    WhoWeAreImageUrl = "/uploads/about/who.png",
                    CreatedAtUtc = new DateTime(2026, 6, 17, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedAtUtc = new DateTime(2026, 6, 17, 0, 0, 0, DateTimeKind.Utc)
                });
            });

            modelBuilder.Entity<AboutUsCount>(entity =>
            {
                entity.Property(x => x.CountValue).HasMaxLength(50).IsRequired();
                entity.Property(x => x.CountTitle).HasMaxLength(100).IsRequired();

                entity.HasOne(x => x.AboutUs)
                    .WithMany(x => x.Counts)
                    .HasForeignKey(x => x.AboutUsId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasData(
                    new AboutUsCount { Id = 1, AboutUsId = 1, CountValue = "6+", CountTitle = "Years", DisplayOrder = 1 },
                    new AboutUsCount { Id = 2, AboutUsId = 1, CountValue = "100+", CountTitle = "Travel Partners", DisplayOrder = 2 },
                    new AboutUsCount { Id = 3, AboutUsId = 1, CountValue = "16+", CountTitle = "Product Managers", DisplayOrder = 3 },
                    new AboutUsCount { Id = 4, AboutUsId = 1, CountValue = "24/7", CountTitle = "Customer Support", DisplayOrder = 4 }
                );
            });

            modelBuilder.Entity<AboutUsTeamMember>(entity =>
            {
                entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
                entity.Property(x => x.Designation).HasMaxLength(200).IsRequired();
                entity.Property(x => x.ImageUrl).HasMaxLength(1000);

                entity.HasOne(x => x.AboutUs)
                    .WithMany(x => x.TeamMembers)
                    .HasForeignKey(x => x.AboutUsId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasData(
                    new AboutUsTeamMember { Id = 1, AboutUsId = 1, Name = "Naveen", Designation = "Lead Developer", ImageUrl = "/uploads/team/naveen.png", DisplayOrder = 1 },
                    new AboutUsTeamMember { Id = 2, AboutUsId = 1, Name = "Rajesh", Designation = "Project Manager", ImageUrl = "/uploads/team/default.png", DisplayOrder = 2 }
                );
            });

            modelBuilder.Entity<ContactQuery>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
                entity.Property(x => x.Email).HasMaxLength(150).IsRequired();
                entity.Property(x => x.PhoneNo).HasMaxLength(30);
                entity.Property(x => x.Subject).HasMaxLength(200).IsRequired();
                entity.Property(x => x.Message).HasMaxLength(2000).IsRequired();
                entity.Property(x => x.Status).HasMaxLength(50).HasDefaultValue("Pending");
                entity.Property(x => x.CreatedAtUtc).IsRequired();
                entity.Property(x => x.UpdatedAtUtc).IsRequired();
            });

            modelBuilder.Entity<BusBooking>(entity =>
            {
                entity.ToTable("bus_bookings");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.BusNumber).HasMaxLength(20).IsRequired();
                entity.Property(x => x.OperatorName).HasMaxLength(120).IsRequired();
                entity.Property(x => x.BusType).HasMaxLength(40).IsRequired();
                entity.Property(x => x.FromCity).HasMaxLength(80).IsRequired();
                entity.Property(x => x.ToCity).HasMaxLength(80).IsRequired();
                entity.Property(x => x.BoardingPoint).HasMaxLength(120).IsRequired();
                entity.Property(x => x.DroppingPoint).HasMaxLength(120).IsRequired();
                entity.Property(x => x.PriceInr).HasPrecision(10, 2);
                entity.HasIndex(x => new { x.BusNumber, x.FromCity, x.ToCity, x.DepartureTime }).IsUnique();
            });

            modelBuilder.Entity<FlightReservation>(entity =>
            {
                entity.ToTable("flight_reservations");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.BookingReference).HasMaxLength(40).IsRequired();
                entity.Property(x => x.Pnr).HasMaxLength(20).IsRequired().HasDefaultValue("");
                entity.Property(x => x.UserId).HasMaxLength(80).IsRequired();
                entity.Property(x => x.PassengerName).HasMaxLength(120).IsRequired();
                entity.Property(x => x.PassengerPhone).HasMaxLength(30).IsRequired();
                entity.Property(x => x.PassengerEmail).HasMaxLength(150);
                entity.Property(x => x.TravelClass).HasMaxLength(30).IsRequired();
                entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
                entity.Property(x => x.CancellationReason).HasMaxLength(300);
                entity.Property(x => x.CancellationChargeInr).HasPrecision(10, 2);
                entity.Property(x => x.RefundAmountInr).HasPrecision(10, 2);
                entity.Property(x => x.TotalPriceInr).HasPrecision(10, 2);
                entity.Property(x => x.CustomerFareInr).HasPrecision(10, 2);
                entity.Property(x => x.NetFareInr).HasPrecision(10, 2);
                entity.Property(x => x.DiscountAmountInr).HasPrecision(10, 2);
                entity.Property(x => x.CouponCode).HasMaxLength(40);
                entity.Property(x => x.SupplierBaseFare).HasPrecision(10, 2);
                entity.Property(x => x.SupplierTaxAmount).HasPrecision(10, 2);
                entity.Property(x => x.SupplierTotalFare).HasPrecision(10, 2);
                entity.Property(x => x.MarkupAmount).HasPrecision(10, 2);
                entity.Property(x => x.PromotionName).HasMaxLength(120);
                entity.Property(x => x.PromotionDiscount).HasPrecision(10, 2);
                entity.Property(x => x.CouponDiscount).HasPrecision(10, 2);
                entity.Property(x => x.FinalAmount).HasPrecision(10, 2);
                entity.Property(x => x.GdsPnr).HasMaxLength(20);
                entity.Property(x => x.TicketStatus).HasMaxLength(30);
                entity.Property(x => x.SrdvChangeRequestId).HasMaxLength(50);
                entity.Property(x => x.SrdvType).HasMaxLength(50);
                entity.Property(x => x.SrdvIndex).HasMaxLength(100);
                entity.HasIndex(x => x.BookingReference).IsUnique();
                entity.HasIndex(x => x.UserId);
                entity.HasIndex(x => x.PassengerPhone);
            });

            modelBuilder.Entity<BusReservation>(entity =>
            {
                entity.ToTable("bus_reservations");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.BookingReference).HasMaxLength(40).IsRequired();
                entity.Property(x => x.Pnr).HasMaxLength(20).IsRequired().HasDefaultValue("");
                entity.Property(x => x.UserId).HasMaxLength(80).IsRequired();
                entity.Property(x => x.PassengerName).HasMaxLength(120).IsRequired();
                entity.Property(x => x.PassengerPhone).HasMaxLength(30).IsRequired();
                entity.Property(x => x.PassengerEmail).HasMaxLength(150);
                entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
                entity.Property(x => x.CancellationReason).HasMaxLength(300);
                entity.Property(x => x.TotalPriceInr).HasPrecision(10, 2);
                entity.Property(x => x.CustomerFareInr).HasPrecision(10, 2);
                entity.Property(x => x.NetFareInr).HasPrecision(10, 2);
                entity.Property(x => x.DiscountAmountInr).HasPrecision(10, 2);
                entity.Property(x => x.ConvenienceFeeInr).HasPrecision(10, 2);
                entity.Property(x => x.CouponCode).HasMaxLength(40);
                entity.Property(x => x.CancellationChargeInr).HasPrecision(10, 2);
                entity.Property(x => x.RefundAmountInr).HasPrecision(10, 2);
                entity.HasIndex(x => x.BookingReference).IsUnique();
                entity.HasIndex(x => x.UserId);
                entity.HasIndex(x => x.PassengerPhone);
                entity.HasOne(x => x.BusBooking)
                    .WithMany()
                    .HasForeignKey(x => x.BusBookingId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<BusReservationPassenger>(entity =>
            {
                entity.ToTable("bus_reservation_passengers");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.FullName).HasMaxLength(120).IsRequired();
                entity.Property(x => x.Gender).HasMaxLength(20).IsRequired();
                entity.Property(x => x.SeatNumber).HasMaxLength(10);
                entity.HasIndex(x => x.BusReservationId);
                entity.HasIndex(x => new { x.BusReservationId, x.SeatNumber }).IsUnique();
                entity.HasOne(x => x.BusReservation)
                    .WithMany()
                    .HasForeignKey(x => x.BusReservationId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<FlightReservationPassenger>(entity =>
            {
                entity.ToTable("flight_reservation_passengers");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.FullName).HasMaxLength(120).IsRequired();
                entity.Property(x => x.PassengerType).HasMaxLength(20).IsRequired();
                entity.Property(x => x.Gender).HasMaxLength(20).IsRequired();
                entity.Property(x => x.SeatNumber).HasMaxLength(10);
                entity.HasIndex(x => x.FlightReservationId);
                entity.HasOne(x => x.FlightReservation)
                    .WithMany()
                    .HasForeignKey(x => x.FlightReservationId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Traveler>(entity =>
            {
                entity.ToTable("travelers");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.UserId).HasMaxLength(80).IsRequired();
                entity.Property(x => x.Type).HasMaxLength(20).IsRequired();
                entity.Property(x => x.Title).HasMaxLength(20).IsRequired();
                entity.Property(x => x.FirstName).HasMaxLength(80).IsRequired();
                entity.Property(x => x.LastName).HasMaxLength(80).IsRequired();
                entity.Property(x => x.Gender).HasMaxLength(20).IsRequired();
                entity.Property(x => x.Email).HasMaxLength(150).IsRequired();
                entity.Property(x => x.PhoneNo).HasMaxLength(30).IsRequired();
                entity.Property(x => x.PassportNo).HasMaxLength(40);
                entity.Property(x => x.Country).HasMaxLength(80).IsRequired();
                entity.HasIndex(x => x.UserId);
                entity.HasIndex(x => x.PhoneNo);
                entity.HasIndex(x => x.Email);
                entity.HasIndex(x => x.Type);
            });

            modelBuilder.Entity<FlightRouteStat>(entity =>
            {
                entity.ToTable("flight_route_stats");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.FromCity).HasMaxLength(80).IsRequired();
                entity.Property(x => x.ToCity).HasMaxLength(80).IsRequired();
                entity.HasIndex(x => new { x.FromCity, x.ToCity }).IsUnique();
            });

            modelBuilder.Entity<BusRouteStat>(entity =>
            {
                entity.ToTable("bus_route_stats");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.FromCity).HasMaxLength(80).IsRequired();
                entity.Property(x => x.ToCity).HasMaxLength(80).IsRequired();
                entity.HasIndex(x => new { x.FromCity, x.ToCity }).IsUnique();
            });
            modelBuilder.Entity<PlaceSearchStat>(entity =>
            {
                entity.ToTable("place_search_stats");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Id).HasColumnName("id");
                entity.Property(x => x.CityName).HasColumnName("city_name").HasMaxLength(150).IsRequired();
                entity.Property(x => x.TripType).HasColumnName("trip_type").HasMaxLength(20).IsRequired();
                entity.Property(x => x.SelectionCount).HasColumnName("selection_count");
                entity.HasIndex(x => new { x.CityName, x.TripType }).IsUnique();
            });
            modelBuilder.Entity<BusDiscount>(entity =>
            {
                entity.ToTable("bus_discounts");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.DiscountType).HasMaxLength(20).IsRequired();
                entity.Property(x => x.Value).HasPrecision(10, 2);
                entity.Property(x => x.UpdatedBy).HasMaxLength(120).IsRequired();
                entity.Property(x => x.Remark).HasMaxLength(300);
                entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            });
            modelBuilder.Entity<BusBlockedSeatPrice>(entity =>
            {
                entity.HasIndex(x => x.TraceId);
            });

            modelBuilder.Entity<BusCoupon>(entity =>
            {
                entity.ToTable("bus_coupons");
                entity.Property(x => x.MinBookingAmount).HasPrecision(10, 2).HasDefaultValue(0);
                entity.Property(x => x.MaxUsagePerUser).IsRequired().HasDefaultValue(1);
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Value).HasPrecision(10, 2);
                entity.Property(x => x.CouponType).HasMaxLength(20).IsRequired();
                entity.Property(x => x.CouponCode).HasMaxLength(40).IsRequired();
                entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
                entity.Property(x => x.Remark).HasMaxLength(300);
                entity.HasIndex(x => x.CouponCode).IsUnique();
            });

            modelBuilder.Entity<BusCouponUsage>(entity =>
            {
                entity.ToTable("bus_coupon_usages");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.UserId).HasMaxLength(50).IsRequired();
                entity.HasIndex(x => new { x.CouponCode, x.UserId });
                entity.Property(x => x.CouponCode).HasMaxLength(40).IsRequired();
                entity.Property(x => x.CouponType).HasMaxLength(20).IsRequired();
                entity.Property(x => x.BookingStatus).HasMaxLength(20).IsRequired();
                entity.Property(x => x.TotalFareInr).HasPrecision(10, 2);
                entity.Property(x => x.CouponValue).HasPrecision(10, 2);
                entity.Property(x => x.CouponAmountInr).HasPrecision(10, 2);
                entity.HasOne(x => x.BusReservation)
                    .WithMany()
                    .HasForeignKey(x => x.BusReservationId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<BusConvenienceFee>(entity =>
            {
                entity.ToTable("bus_convenience_fee");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.FeeInr).HasPrecision(10, 2);
                entity.Property(x => x.UpdatedBy).HasMaxLength(120).IsRequired();
                entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            });

            modelBuilder.Entity<BusSearchLog>(entity =>
            {
                entity.ToTable("bus_search_logs");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.UserId).HasMaxLength(80);
                entity.Property(x => x.UserOrGuestId).HasMaxLength(80);
                entity.Property(x => x.IsGuest);
                entity.Property(x => x.FromCity).HasMaxLength(80).IsRequired();
                entity.Property(x => x.ToCity).HasMaxLength(80).IsRequired();
                entity.HasIndex(x => x.SearchedAtUtc);
                entity.HasIndex(x => new { x.FromCity, x.ToCity });
            });

            modelBuilder.Entity<BusMarkupSetting>(entity =>
            {
                entity.ToTable("bus_markup_settings");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.SeatType).HasMaxLength(50).IsRequired();
                entity.Property(x => x.Value).HasPrecision(10, 2);
                entity.Property(x => x.MarkupType).HasMaxLength(20).IsRequired();
                entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
                entity.Property(x => x.UpdatedBy).HasMaxLength(120);
                entity.Property(x => x.Remark).HasMaxLength(300);
            });

            modelBuilder.Entity<BusGstSetting>(entity =>
            {
                entity.ToTable("bus_gst_settings");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.GstCategory).HasMaxLength(50).IsRequired();
                entity.Property(x => x.GstPercent).HasPrecision(10, 2);
                entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
                entity.Property(x => x.UpdatedBy).HasMaxLength(120);
                entity.Property(x => x.Remark).HasMaxLength(300);
            });

            modelBuilder.Entity<FlightDiscount>(entity =>
            {
                entity.ToTable("flight_discounts");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Value).HasPrecision(10, 2);
                entity.Property(x => x.DiscountType).HasMaxLength(20).IsRequired();
                entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
                entity.Property(x => x.UpdatedBy).HasMaxLength(120).IsRequired();
                entity.Property(x => x.Remark).HasMaxLength(300);
                entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            });

            modelBuilder.Entity<FlightRemark>(entity =>
            {
                entity.ToTable("flight_remarks");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.SourceType).HasMaxLength(60).IsRequired();
                entity.Property(x => x.UpdatedBy).HasMaxLength(120).IsRequired();
                entity.Property(x => x.Remark).HasMaxLength(500).IsRequired();
                entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            });

            modelBuilder.Entity<FlightCoupon>(entity =>
            {
                entity.ToTable("flight_coupons");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Value).HasPrecision(10, 2);
                entity.Property(x => x.CouponType).HasMaxLength(20).IsRequired();
                entity.Property(x => x.CouponCode).HasMaxLength(40).IsRequired();
                entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
                entity.Property(x => x.Remark).HasMaxLength(300);
                entity.HasIndex(x => x.CouponCode).IsUnique();
            });

            modelBuilder.Entity<FlightCouponUsage>(entity =>
            {
                entity.ToTable("flight_coupon_usages");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.CouponCode).HasMaxLength(40).IsRequired();
                entity.Property(x => x.CouponType).HasMaxLength(20).IsRequired();
                entity.Property(x => x.BookingStatus).HasMaxLength(20).IsRequired();
                entity.Property(x => x.TotalFareInr).HasPrecision(10, 2);
                entity.Property(x => x.CouponValue).HasPrecision(10, 2);
                entity.Property(x => x.CouponAmountInr).HasPrecision(10, 2);
                entity.HasOne(x => x.FlightReservation)
                    .WithMany()
                    .HasForeignKey(x => x.FlightReservationId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<FlightConvenienceFee>(entity =>
            {
                entity.ToTable("flight_convenience_fee");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.AmountType).HasMaxLength(20).IsRequired();
                entity.Property(x => x.Value).HasPrecision(10, 2);
                entity.Property(x => x.UpdatedBy).HasMaxLength(120).IsRequired();
                entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            });

            modelBuilder.Entity<FlightSearchLog>(entity =>
            {
                entity.ToTable("flight_search_logs");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.UserId).HasMaxLength(80);
                entity.Property(x => x.UserOrGuestId).HasMaxLength(80);
                entity.Property(x => x.IsGuest);
                entity.Property(x => x.FromCity).HasMaxLength(80).IsRequired();
                entity.Property(x => x.ToCity).HasMaxLength(80).IsRequired();
                entity.Property(x => x.TripType).HasMaxLength(20).IsRequired();
                entity.HasIndex(x => x.SearchedAtUtc);
                entity.HasIndex(x => new { x.FromCity, x.ToCity });
            });

            modelBuilder.Entity<PendingAirline>(entity =>
            {
                entity.ToTable("pending_airlines");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.AirlineCode).HasMaxLength(10).IsRequired();
                entity.Property(x => x.FareType).HasMaxLength(40).IsRequired();
                entity.Property(x => x.UpdatedBy).HasMaxLength(120).IsRequired();
                entity.Property(x => x.Remark).HasMaxLength(300);
            });

            modelBuilder.Entity<Airline>(entity =>
            {
                entity.ToTable("airlines");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
                entity.Property(x => x.Code).HasMaxLength(10).IsRequired();
                entity.Property(x => x.ImageUrl).HasMaxLength(500);
                entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
                entity.HasIndex(x => x.Code).IsUnique();
            });

            modelBuilder.Entity<AirlineWebcheckLink>(entity =>
            {
                entity.ToTable("airline_webcheck_links");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Airline).HasMaxLength(120).IsRequired();
                entity.Property(x => x.AirlineCode).HasMaxLength(10).IsRequired();
                entity.Property(x => x.Url).HasMaxLength(500).IsRequired();
                entity.HasIndex(x => x.AirlineCode);
            });

            modelBuilder.Entity<PopularDestination>(entity =>
            {
                entity.ToTable("popular_destinations");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Title).HasMaxLength(120).IsRequired();
                entity.Property(x => x.SubTitle).HasMaxLength(180).IsRequired();
                entity.Property(x => x.ImageUrl).HasMaxLength(500);
                entity.Property(x => x.Category).HasMaxLength(80).IsRequired();
                entity.Property(x => x.Placement).HasMaxLength(20).IsRequired();
                entity.Property(x => x.Url).HasMaxLength(500);
                entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            });

            modelBuilder.Entity<FlightCancellationRequest>(entity =>
            {
                entity.ToTable("flight_cancellation_requests");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.CancellationStatus).HasMaxLength(20).IsRequired();
                entity.Property(x => x.CustomerRefundStatus).HasMaxLength(20).IsRequired();
                entity.Property(x => x.AdminRefundStatus).HasMaxLength(20).IsRequired();
                entity.Property(x => x.CustomerRefundAmountInr).HasPrecision(10, 2);
                entity.Property(x => x.CustomerCancellationChargeInr).HasPrecision(10, 2);
                entity.Property(x => x.CustomerServiceChargeInr).HasPrecision(10, 2);
                entity.Property(x => x.AdminRefundAmountInr).HasPrecision(10, 2);
                entity.Property(x => x.AdminCancellationChargeInr).HasPrecision(10, 2);
                entity.Property(x => x.AdminServiceChargeInr).HasPrecision(10, 2);
                entity.Property(x => x.SupplierRemark).HasMaxLength(500);
                entity.Property(x => x.CustomerRemark).HasMaxLength(500);
                entity.Property(x => x.AdminRemark).HasMaxLength(500);
                entity.HasOne(x => x.FlightReservation)
                    .WithMany()
                    .HasForeignKey(x => x.FlightReservationId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<FlightAmendmentRequest>(entity =>
            {
                entity.ToTable("flight_amendment_requests");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.AmendmentStatus).HasMaxLength(20).IsRequired();
                entity.Property(x => x.SupplierRemark).HasMaxLength(500);
                entity.Property(x => x.CustomerRemark).HasMaxLength(500);
                entity.Property(x => x.AdminRemark).HasMaxLength(500);
                entity.HasOne(x => x.FlightReservation)
                    .WithMany()
                    .HasForeignKey(x => x.FlightReservationId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<HotelReservation>(entity =>
            {
                entity.ToTable("hotel_reservations");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.BookingReference).HasMaxLength(40).IsRequired();
                entity.Property(x => x.ProviderBookingId).HasMaxLength(80);
                entity.Property(x => x.UserId).HasMaxLength(80).IsRequired();
                entity.Property(x => x.HotelId).HasMaxLength(80).IsRequired();
                entity.Property(x => x.HotelName).HasMaxLength(200).IsRequired();
                entity.Property(x => x.OfferId).HasMaxLength(120).IsRequired();
                entity.Property(x => x.CityCode).HasMaxLength(10).IsRequired();
                entity.Property(x => x.GuestName).HasMaxLength(120).IsRequired();
                entity.Property(x => x.GuestEmail).HasMaxLength(150).IsRequired();
                entity.Property(x => x.GuestPhone).HasMaxLength(30).IsRequired();
                entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
                entity.Property(x => x.CancellationReason).HasMaxLength(300);
                entity.Property(x => x.Price).HasPrecision(10, 2);
                entity.Property(x => x.NetPrice).HasPrecision(10, 2);
                entity.Property(x => x.MarkupAmount).HasPrecision(10, 2);
                entity.Property(x => x.BasePrice).HasPrecision(10, 2);
                entity.Property(x => x.ConvenienceFee).HasPrecision(10, 2);
                entity.Property(x => x.TotalPrice).HasPrecision(10, 2);
                entity.Property(x => x.CancellationCharges).HasPrecision(10, 2);
                entity.Property(x => x.RefundAmount).HasPrecision(10, 2);
                entity.Property(x => x.CouponDiscount).HasPrecision(10, 2);
                entity.Property(x => x.CouponCode).HasMaxLength(40);
                entity.Property(x => x.TraceId).HasMaxLength(80);
                entity.Property(x => x.SrdvBookingId).HasMaxLength(80);
                entity.Property(x => x.GuestNationality).HasMaxLength(10).IsRequired();
                entity.Property(x => x.RoomTypeName).HasMaxLength(200);
                entity.Property(x => x.Currency).HasMaxLength(10).IsRequired();
                entity.HasIndex(x => x.BookingReference).IsUnique();
                entity.HasIndex(x => x.UserId);
                entity.HasIndex(x => x.HotelId);
                entity.HasIndex(x => x.Status);
            });

            modelBuilder.Entity<HotelPricingSetting>(entity =>
            {
                entity.ToTable("hotel_pricing_settings");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.MarkupType).HasMaxLength(20).IsRequired();
                entity.Property(x => x.MarkupValue).HasPrecision(10, 2);
            });

            modelBuilder.Entity<FlightPromotion>(entity =>
            {
                entity.ToTable("flight_promotions");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
                entity.Property(x => x.Description).HasMaxLength(500);
                entity.Property(x => x.DiscountType).HasConversion<string>().HasMaxLength(20).IsRequired();
                entity.Property(x => x.DiscountValue).HasPrecision(10, 2);
                entity.Property(x => x.MaximumDiscount).HasPrecision(10, 2);
                entity.Property(x => x.MinimumFare).HasPrecision(10, 2);
            });

            modelBuilder.Entity<FlightPromotionCondition>(entity =>
            {
                entity.ToTable("flight_promotion_conditions");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.ConditionType).HasConversion<string>().HasMaxLength(50).IsRequired();
                entity.Property(x => x.Operator).HasMaxLength(50).IsRequired();
                entity.Property(x => x.Value).HasMaxLength(200).IsRequired();
                entity.HasOne(x => x.FlightPromotion)
                    .WithMany(x => x.Conditions)
                    .HasForeignKey(x => x.FlightPromotionId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<FlightPromotionUsage>(entity =>
            {
                entity.ToTable("flight_promotion_usages");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.UserId).HasMaxLength(80).IsRequired();
                entity.Property(x => x.DiscountAmount).HasPrecision(10, 2);
                entity.HasOne(x => x.FlightPromotion)
                    .WithMany()
                    .HasForeignKey(x => x.FlightPromotionId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(x => x.FlightReservation)
                    .WithMany()
                    .HasForeignKey(x => x.ReservationId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<FlightMarkupRule>(entity =>
            {
                entity.ToTable("flight_markup_rules");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.AirlineCode).HasMaxLength(10).IsRequired();
                entity.Property(x => x.TripType).HasConversion<string>().HasMaxLength(20).IsRequired();
                entity.Property(x => x.MarkupType).HasConversion<string>().HasMaxLength(20).IsRequired();
                entity.Property(x => x.MarkupValue).HasPrecision(10, 2);
            });

            modelBuilder.Entity<HotelMarkupRule>(entity =>
            {
                entity.ToTable("hotel_markup_rules");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.RuleName).HasMaxLength(150).IsRequired();
                entity.Property(x => x.CityCode).HasMaxLength(50).IsRequired();
                entity.Property(x => x.HotelCode).HasMaxLength(100).IsRequired();
                entity.Property(x => x.UserType).HasMaxLength(20).IsRequired();
                entity.Property(x => x.MarkupType).HasMaxLength(20).IsRequired();
                entity.Property(x => x.MarkupValue).HasPrecision(12, 2);
            });

            modelBuilder.Entity<FlightConvenienceFeeRule>(entity =>
            {
                entity.ToTable("flight_convenience_fee_rules");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.TripType).HasConversion<string>().HasMaxLength(20).IsRequired();
                entity.Property(x => x.FeeType).HasMaxLength(20).IsRequired();
                entity.Property(x => x.FeeValue).HasPrecision(10, 2);
            });

            modelBuilder.Entity<MenuItem>(entity =>
            {
                entity.ToTable("menu_items");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
                entity.Property(x => x.Slug).HasMaxLength(200).IsRequired();
                entity.Property(x => x.DisplayTitle).HasMaxLength(200).IsRequired();
                entity.Property(x => x.Module).HasMaxLength(20).IsRequired();
                entity.Property(x => x.Location).HasMaxLength(20).IsRequired();
                entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
                entity.HasIndex(x => new { x.Module, x.Location, x.Slug }).IsUnique();

                // Initial Seeding matching frontend specifications
                entity.HasData(
                    new MenuItem { Id = 1, Name = "Support", Slug = "support", DisplayTitle = "Support", Order = 2, Module = "B2C", Location = "header", Status = "active", CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), UpdatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                    new MenuItem { Id = 2, Name = "Home", Slug = "home", DisplayTitle = "Home", Order = 1, Module = "B2C", Location = "header", Status = "active", CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), UpdatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                    new MenuItem { Id = 3, Name = "Policies", Slug = "policies", DisplayTitle = "Policies", Order = 3, Module = "B2C", Location = "footer", Status = "active", CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), UpdatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                    new MenuItem { Id = 4, Name = "Quick Links", Slug = "quick-links", DisplayTitle = "Quick Links", Order = 2, Module = "B2C", Location = "footer", Status = "active", CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), UpdatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                    new MenuItem { Id = 5, Name = "Services", Slug = "services", DisplayTitle = "Services", Order = 1, Module = "B2C", Location = "footer", Status = "active", CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), UpdatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
                );
            });

            modelBuilder.Entity<DepositRequest>(entity =>
            {
                entity.ToTable("deposit_requests");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Amount).HasPrecision(18, 2);
                entity.Property(x => x.Type).HasMaxLength(20).IsRequired();
                entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
                entity.Property(x => x.UserRemark).HasMaxLength(500);
                entity.Property(x => x.AdminRemark).HasMaxLength(500);
                entity.HasOne(x => x.User)
                    .WithMany()
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // =============================
            // Security Module Configurations
            // =============================
            modelBuilder.Entity<IpAccessRule>(entity =>
            {
                entity.HasIndex(x => new { x.IpAddress, x.Status }).HasDatabaseName("idx_ip_status");
                entity.HasIndex(x => new { x.ExpiresAt, x.Status }).HasDatabaseName("idx_expires");
                entity.HasIndex(x => x.IpAddress).IsUnique();
            });

            modelBuilder.Entity<UserSession>(entity =>
            {
                entity.HasIndex(x => new { x.UserId, x.Status }).HasDatabaseName("idx_user_status");
                entity.HasIndex(x => new { x.ExpiresAt, x.Status }).HasDatabaseName("idx_session_expiry");
            });

            modelBuilder.Entity<AdminSession>(entity =>
            {
                entity.HasIndex(x => new { x.AdminId, x.Status }).HasDatabaseName("idx_admin_status");
                entity.HasIndex(x => new { x.ExpiresAt, x.Status }).HasDatabaseName("idx_admin_session_expiry");
            });

            modelBuilder.Entity<OtpRecord>(entity =>
            {
                entity.HasIndex(x => new { x.Identifier, x.Purpose, x.IsVerified }).HasDatabaseName("idx_identifier_purpose");
            });

            modelBuilder.Entity<SecurityNotificationMapping>(entity =>
            {
                entity.HasIndex(x => x.EventType).IsUnique();
            });

            modelBuilder.Entity<SecurityNotification>(entity =>
            {
                entity.HasIndex(x => new { x.Status, x.CreatedAt }).HasDatabaseName("idx_notif_status");
                entity.HasIndex(x => new { x.CooldownKey, x.CreatedAt }).HasDatabaseName("idx_cooldown");
            });

            modelBuilder.Entity<SecurityAuditLog>(entity =>
            {
                entity.HasIndex(x => new { x.EventType, x.CreatedAt }).HasDatabaseName("idx_audit_event");
                entity.HasIndex(x => new { x.IpAddress, x.CreatedAt }).HasDatabaseName("idx_audit_ip");
            });
        }
    }
}
