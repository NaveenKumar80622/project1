# Notification Templates Catalogue

This document defines all the notification templates (Email, SMS, and WhatsApp) used in the PickNBook transactional notification system. Each template is identified by a unique `TemplateKey` and expects a specific set of variables (payload) to render correctly.

## 1. Authentication (OTPs)

### 1.1 Registration OTP
**TemplateKey**: `REGISTRATION_OTP`
**Variables**: `{OtpCode}`, `{ExpiryMinutes}` (or `${var1}`, `${var2}`)
**Header / SenderId**: `PICNBK`
**DLT Content ID**: `1777178972156267667`
**Provider Template**: `NEWREGISTRATION_OTP` (ID: `1562926`)

**Email Template**
*Subject*: Welcome to PickNBook! Verify your email
*Body*:
```html
<h3>Welcome to PickNBook!</h3>
<p>Your one-time registration code is: <strong>{OtpCode}</strong></p>
<p>This code expires in 2 minutes.</p>
```

**SMS Template**
*Body*: `Your Pick&Book Registration OTP is {OtpCode}. It is valid for {ExpiryMinutes} minutes. Do not share this OTP with anyone.`
*(Provider format: `Your Pick&Book Registration OTP is ${var1}. It is valid for ${var2} minutes. Do not share this OTP with anyone.`)*

---

### 1.2 Login OTP
**TemplateKey**: `LOGIN_OTP`
**Variables**: `{OtpCode}`, `{ExpiryMinutes}` (or `${var1}`, `${var2}`)
**Header / SenderId**: `PICNBK`
**DLT Content ID**: `1777178972075919532`
**Provider Template**: `NewLOGIN_OTP` (ID: `1562918`)

**Email Template**
*Subject*: PickNBook Login Verification
*Body*:
```html
<h3>Login Verification</h3>
<p>Your login OTP is: <strong>{OtpCode}</strong></p>
<p>If you did not request this, please secure your account.</p>
```

**SMS Template**
*Body*: `Your Pick&Book login OTP is {OtpCode}. It is valid for {ExpiryMinutes} minutes. Do not share this OTP with anyone.`
*(Provider format: `Your Pick&Book login OTP is ${var1}. It is valid for ${var2} minutes. Do not share this OTP with anyone.`)*

---

### 1.3 Password Reset OTP (User & B2B)
**TemplateKey**: `PASSWORD_RESET_OTP`
**Variables**: `{OtpCode}`, `{ExpiryMinutes}` (or `${var1}`, `${var2}`)
**Header / SenderId**: `PICNBK`
**DLT Content ID**: `1777178972177543618`
**Provider Template**: `NewPassword Reset` (ID: `1562930`)

**Email Template**
*Subject*: PickNBook Password Reset Request
*Body*:
```html
<h3>Password Reset</h3>
<p>You requested to reset your password. Your OTP is: <strong>{OtpCode}</strong></p>
<p>If you did not request this, ignore this email.</p>
```

**SMS Template**
*Body*: `Your Pick&Book password reset OTP is {OtpCode}. It is valid for {ExpiryMinutes} minutes. Do not share this OTP.`
*(Provider format: `Your Pick&Book password reset OTP is ${var1}. It is valid for ${var2} minutes. Do not share this OTP.`)*

---

### 1.4 Admin OTP (Login/Reset)
**TemplateKey**: `ADMIN_OTP`
**Variables**: `{OtpCode}`

**Email Template**
*Subject*: PickNBook Admin Authorization
*Body*:
```html
<h3>Admin Verification</h3>
<p>Your admin authorization code is: <h2 style='color:#2d89ef'>{OtpCode}</h2></p>
<p>This OTP expires in 5 minutes.</p>
```

**SMS/WhatsApp Template**
*Body*: `PickNBook Admin OTP: {OtpCode}. Valid for 5 mins.`

---

## 2. Flight Bookings

### 2.1 Flight Booking Confirmed
**TemplateKey**: `FLIGHT_BOOKING_CONFIRMED`
**Variables**: `{Pnr}`, `{Name}`, `{Amount}`

**Email Template**
*Subject*: Flight Booking Confirmed - {Pnr}
*Body*:
```html
<h3>Flight Booking Confirmed!</h3>
<p>Dear {Name},</p>
<p>Your flight booking is confirmed. Your PNR is <strong>{Pnr}</strong>.</p>
<p>Total Paid: INR {Amount}</p>
<p>Your e-ticket is attached or can be downloaded from your account.</p>
```

**SMS Template**
*TemplateKey*: `FLIGHT_BOOKING_CONFIRMED` (Alias: `FLIGHT_BOOKING_CONFIRMED_SMS`)
*EventType*: `FlightBookingSuccess`
*Channel*: `SMS`
*DLT Content ID*: `1777178996708609183`
*DLT Reference Number*: `11-1REDXMUAS6QYZ`
*Provider Template ID*: `1563322`
*Provider Label*: `FLIGHT_BOOKING_CONFIRMED`
*Sender Header*: `PICNBK`
*Variables*: `${var1}` / `{Pnr}`, `${var2}` / `{Flight}`, `${var3}` / `{Route}`, `${var4}` / `{Date}`
*Body*: `Pick&Book: Flight booking confirmed. PNR: {Pnr}. Flight: {Flight}. Route: {Route}. Date: {Date}.`
*(Provider format: `Pick&Book: Flight booking confirmed. PNR: ${var1}. Flight: ${var2}. Route: ${var3}. Date: ${var4}.`)*
*Sample Content*: `Pick&Book: Flight booking confirmed. PNR: X4K9LM. Flight: 6E-204. Route: DEL-BOM. Date: 25/09/2026 10:30 AM.`

---

### 2.2 Flight Booking Failed
**TemplateKey**: `FLIGHT_BOOKING_FAILED`
**Variables**: `{Reason}`, `{Amount}`

**Email Template**
*Subject*: Action Required: Flight Booking Failed
*Body*:
```html
<h3>Flight Booking Failed</h3>
<p>Unfortunately, your flight booking could not be completed.</p>
<p>Reason: {Reason}</p>
<p>Any amount deducted (INR {Amount}) will be refunded automatically.</p>
```

**SMS Template**
*TemplateKey*: `FLIGHT_BOOKING_FAILED` (Alias: `FLIGHT_BOOKING_FAILED_SMS`)
*EventType*: `FlightBookingFailed`
*Channel*: `SMS`
*DLT Content ID*: `1777178996725418622`
*DLT Reference Number*: `11-1REDXMUASACOA`
*Provider Template ID*: `1563303`
*Provider Label*: `FLIGHT_BOOKING_FAILED`
*Sender Header*: `PICNBK`
*Variables*: `${var1}` / `{Reference}`, `${var2}` / `{Reason}`
*Body*: `Pick&Book: Flight booking could not be completed. Ref: {Reference}. Reason: {Reason}.`
*(Provider format: `Pick&Book: Flight booking could not be completed. Ref: ${var1}. Reason: ${var2}.`)*
*Sample Content*: `Pick&Book: Flight booking could not be completed. Ref: PNBF2609210042. Reason: Fare expired.`

---

### 2.3 Flight Booking Cancelled
**TemplateKey**: `FLIGHT_BOOKING_CANCELLED` (Alias: `FLIGHT_BOOKING_CANCELLED_SMS`)
**EventType**: `FlightBookingCancelled`
**Channel**: `SMS`
**DLT Content ID**: `1777178996735449379`
**DLT Reference Number**: `11-1RDRLMUASCI2L`
**Provider Template ID**: `1563321`
**Provider Label**: `FLIGHT_BOOKING_CANCELLED`
**Sender Header**: `PICNBK`
**Variables**: `${var1}` / `{Reference}` / `{Pnr}`, `${var2}` / `{Status}`
**Body**: `Pick&Book: Flight booking {Reference} has been cancelled. Cancellation status: {Status}.`
*(Provider format: `Pick&Book: Flight booking ${var1} has been cancelled. Cancellation status: ${var2}.`)*
*Sample Content*: `Pick&Book: Flight booking X4K9LM has been cancelled. Cancellation status: Confirmed.`

---

## 3. Hotel Bookings

### 3.1 Hotel Booking Confirmed
**TemplateKey**: `HOTEL_BOOKING_CONFIRMED`
**Variables**: `{HotelName}`, `{Name}`, `{Amount}`

**Email Template**
*Subject*: Hotel Booking Confirmed - {HotelName}
*Body*:
```html
<h3>Hotel Booking Confirmed!</h3>
<p>Dear {Name},</p>
<p>Your booking at <strong>{HotelName}</strong> is confirmed.</p>
<p>Total Paid: INR {Amount}</p>
<p>Please check your account for the hotel voucher.</p>
```

**SMS Template**
*TemplateKey*: `HOTEL_BOOKING_CONFIRMED` (Alias: `HOTEL_BOOKING_CONFIRMED_SMS`)
*EventType*: `HotelBookingSuccess`
*Channel*: `SMS`
*DLT Content ID*: `1777178997026681565`
*DLT Reference Number*: `11-1REDXMUAU2X8G`
*Provider Template ID*: `1563318`
*Provider Label*: `newHOTEL_BOOKING_CONFIRMED`
*Header (Sender ID)*: `PICNBK`
*Variables*: `${var1}` / `{Reference}`, `${var2}` / `{Hotel}`, `${var3}` / `{CheckIn}`, `${var4}` / `{CheckOut}`
*Body*: `Pick&Book: Hotel booking confirmed. Ref {Reference}. Hotel: {Hotel}. Check-in: {CheckIn}. Check-out: {CheckOut}.`
*(Provider format: `Pick&Book: Hotel booking confirmed. Ref ${var1}. Hotel: ${var2}. Check-in: ${var3}. Check-out: ${var4}.`)*
*Sample Content*: `Pick&Book: Hotel booking confirmed. Ref PNB26091000125. Hotel: Grand Hyderabad Hotel. Check-in: 20/09/2026. Check-out: 22/09/2026.`

---

### 3.2 Hotel Booking Failed
**TemplateKey**: `HOTEL_BOOKING_FAILED`
**Variables**: `{Reason}`, `{Amount}`

**Email Template**
*Subject*: Action Required: Hotel Booking Failed
*Body*:
```html
<h3>Hotel Booking Failed</h3>
<p>Unfortunately, your hotel booking could not be completed.</p>
<p>Reason: {Reason}</p>
<p>Any amount deducted (INR {Amount}) will be refunded automatically.</p>
```

**SMS Template**
*TemplateKey*: `HOTEL_BOOKING_FAILED` (Alias: `HOTEL_BOOKING_FAILED_SMS`)
*EventType*: `HotelBookingFailed`
*Channel*: `SMS`
*DLT Content ID*: `1777178997037555745`
*DLT Reference Number*: `11-1RDRLMUAU5951`
*Provider Template ID*: `1563294`
*Provider Label*: `newHOTEL_BOOKING_FAILED`
*Header (Sender ID)*: `PICNBK`
*Variables*: `${var1}` / `{Reference}`, `${var2}` / `{Reason}`
*Body*: `Pick&Book: Hotel booking could not be completed. Ref {Reference}. Reason: {Reason}.`
*(Provider format: `Pick&Book: Hotel booking could not be completed. Ref ${var1}. Reason: ${var2}.`)*
*Sample Content*: `Pick&Book: Hotel booking could not be completed. Ref PNB26091000125. Reason: Room unavailable.`

---

### 3.3 Hotel Booking Cancelled
**TemplateKey**: `HOTEL_BOOKING_CANCELLED` (Alias: `HOTEL_BOOKING_CANCELLED_SMS`)
**EventType**: `HotelBookingCancelled`
**Channel**: `SMS`
**DLT Content ID**: `1777178997046597829`
**DLT Reference Number**: `11-1REDXMUAU76WQ`
**Provider Template ID**: `1563302`
**Provider Label**: `newHOTEL_BOOKING_CANCELLED`
**Header (Sender ID)**: `PICNBK`
**Variables**: `${var1}` / `{Reference}`, `${var2}` / `{Status}`

**SMS Template**
*Body*: `Pick&Book: Hotel booking {Reference} has been cancelled. Cancellation status: {Status}.`
*(Provider format: `Pick&Book: Hotel booking ${var1} has been cancelled. Cancellation status: ${var2}.`)*
*Sample Content*: `Pick&Book: Hotel booking PNB26091000125 has been cancelled. Cancellation status: Confirmed.`

---

### 3.4 Hotel Check-In Reminder
**TemplateKey**: `HOTEL_CHECKIN_REMINDER` (Alias: `HOTEL_CHECKIN_REMINDER_SMS`)
**EventType**: `HotelCheckInReminder`
**Channel**: `SMS`
**DLT Content ID**: `1777178962486170496`
**DLT Reference Number**: `11-1REDXMU54FOY0`
**Provider Template ID**: `1562675`
**Header (Sender ID)**: `PICNBK`
**Variables**: `${var1}` / `{Hotel}`, `${var2}` / `{CheckIn}`, `${var3}` / `{Reference}`

**SMS Template**
*Body*: `PickNBook reminder: Your check-in at ${var1} is on ${var2}. Booking Ref ${var3}.`
*Sample Content*: `PickNBook reminder: Your check-in at Grand Hyderabad Hotel is on 20/09/2026. Booking Ref PNB26091000125.`

---

## 4. Bus Bookings

### 4.1 Bus Booking Confirmed
**TemplateKey**: `BUS_BOOKING_CONFIRMED`
**Variables**: `{Pnr}`, `{Name}`, `{Amount}`

**Email Template**
*Subject*: Bus Booking Confirmed - {Pnr}
*Body*:
```html
<h3>Bus Booking Confirmed!</h3>
<p>Dear {Name},</p>
<p>Your bus booking is confirmed. PNR: <strong>{Pnr}</strong>.</p>
<p>Total Paid: INR {Amount}</p>
<p>Please log in to download your ticket.</p>
```

**SMS Template**
*TemplateKey*: `BUS_BOOKING_CONFIRMED` (Alias: `BUS_BOOKING_CONFIRMED_SMS`)
*DLT Template ID*: `1777178997159302603`
*Provider Label*: `newBUS_BOOKING_CONFIRMED`
*Provider Template ID*: `1563352`
*Sender Header*: `PICNBK`
*Variables*: `{Reference}` / `${var1}`, `{Pnr}` / `${var2}`, `{Boarding}` / `${var3}`, `{Time}` / `${var4}`
*Body*: `Pick&Book: Bus booking confirmed. Ref {Reference}, Pnr {Pnr}. Boarding: {Boarding} at {Time}.`
*(Provider format: `Pick&Book: Bus booking confirmed. Ref ${var1}, Pnr ${var2}. Boarding: ${var3} at ${var4}.`)*
*Sample Content*: `Pick&Book: Bus booking confirmed. Ref PNB26091000125, Pnr PNR-B784192. Boarding: MGBS Hyderabad at 21/09/2026 06:30 AM.`

---

### 4.2 Bus Booking Failed
**TemplateKey**: `BUS_BOOKING_FAILED`
**Variables**: `{Reason}`, `{Amount}`

**Email Template**
*Subject*: Action Required: Bus Booking Failed
*Body*:
```html
<h3>Bus Booking Failed</h3>
<p>Unfortunately, your bus booking could not be completed.</p>
<p>Reason: {Reason}</p>
<p>Any amount deducted (INR {Amount}) will be refunded automatically.</p>
```

**SMS Template**
*DLT Template ID*: `1777178997190921279`
*Provider Label*: `newBUS_BOOKING_FAILED`
*Sender Header*: `PICNBK`
*Body*: `Pick&Book: Bus booking could not be completed. Ref {Reference}. Reason: {Reason}.`
*(Provider format: `Pick&Book: Bus booking could not be completed. Ref ${var1}. Reason: ${var2}.`)*

---

### 4.3 Bus Boarding Reminder
**TemplateKey**: `BUS_BOARDING_REMINDER` (Alias: `BUS_BOARDING_REMINDER_SMS`)
**EventType**: `BusBoardingReminder`
**Channel**: `SMS`
**DLT Content ID**: `1777178962432512946`
**Provider Template ID**: `1562671`
**Header (Sender ID)**: `PICNBK`
**Variables**: `${var1}` / `{Pnr}`, `${var2}` / `{Date}`, `${var3}` / `{Time}`, `${var4}` / `{Boarding}`

**SMS Template**
*Body*: `PickNBook reminder: Your bus PNR ${var1} departs on ${var2} at ${var3}. Boarding: ${var4}.`
*Sample Content*: `PickNBook reminder: Your bus PNR PNB26091000125 departs on 21/09/2026 at 06:30 AM. Boarding: MGBS Hyderabad.`

---

### 4.4 Booking Cancelled
**TemplateKey**: `BOOKING_CANCELLED`
**EventType**: `BookingCancelled`
**Channel**: `SMS`
**DLT Content ID**: `1777178997200725228`
**Provider Label**: `newBOOKING_CANCELLED`
**Provider Template ID**: `1563353`
**Header (Sender ID)**: `PICNBK`
**Variables**: `{Reference}` / `${var1}`, `{Status}` / `${var2}`

**SMS Template**
*Body*: `Pick&Book: Booking {Reference} has been cancelled. Cancellation status: {Status}.`
*(Provider format: `Pick&Book: Booking ${var1} has been cancelled. Cancellation status: ${var2}.`)*
*Sample Content*: `Pick&Book: Booking PNB26091000125 has been cancelled. Cancellation status: Success.`

---

## 5. Payments & Refunds

### 5.1 Payment Success
**TemplateKey**: `PAYMENT_SUCCESS`
**Variables**: `{Amount}`, `{OrderId}`

**Email Template**
*Subject*: Payment Received - {OrderId}
*Body*:
```html
<h3>Payment Successful</h3>
<p>We have successfully received your payment of INR {Amount} for Order {OrderId}.</p>
<p>Your booking will be processed shortly.</p>
```

**SMS Template**
*DLT Template ID*: `1777178997110621673`
*Provider Label*: `newPAYMENT_SUCCESS`
*Sender Header*: `PICNBK`
*Body*: `Pick&Book: Payment successful. Ref {Reference}. Amount Rs. {Amount}`
*(Provider format: `Pick&Book: Payment successful. Ref ${var1}. Amount Rs. ${var2}`)*

---

### 5.2 Payment Failed
**TemplateKey**: `PAYMENT_FAILED`
**Variables**: `{Amount}`, `{OrderId}`, `{Reason}`

**Email Template**
*Subject*: Payment Failed - {OrderId}
*Body*:
```html
<h3>Payment Failed</h3>
<p>Your payment of INR {Amount} for Order {OrderId} has failed.</p>
<p>Reason: {Reason}</p>
<p>Please try again using a different payment method.</p>
```

**SMS Template**
*DLT Template ID*: `1777178997180999327`
*Provider Label*: `newPAYMENT_FAILED`
*Sender Header*: `PICNBK`
*Body*: `Pick&Book: Payment failed for reference {Reference}. Reason: {Reason}.`
*(Provider format: `Pick&Book: Payment failed for reference ${var1}. Reason: ${var2}.`)*

---

### 5.3 Refund Initiated
**TemplateKey**: `REFUND_INITIATED`
**Variables**: `{Amount}`, `{BookingId}`

**Email Template**
*Subject*: Refund Initiated
*Body*:
```html
<h3>Refund Initiated</h3>
<p>A refund of INR {Amount} for Booking ID {BookingId} has been initiated.</p>
<p>It may take 5-7 business days to reflect in your account.</p>
```

**SMS/WhatsApp Template**
*Body*: `PickNBook has initiated a refund of INR {Amount} for your booking ({BookingId}). Expect it in 5-7 days.`

---

### 5.4 Refund Completed
**TemplateKey**: `REFUND_COMPLETED`
**Variables**: `{Amount}`, `{BookingId}`

**Email Template**
*Subject*: Refund Completed
*Body*:
```html
<h3>Refund Completed</h3>
<p>Your refund of INR {Amount} for Booking ID {BookingId} has been successfully processed.</p>
<p>Please check your bank statement.</p>
```

**SMS Template**
*TemplateKey*: `REFUND_STATUS`
*DLT Template ID*: `1777178997089847183`
*Provider Label*: `newREFUND_STATUS`
*Provider Template ID*: `1563351`
*Sender Header*: `PICNBK`
*Variables*: `{Status}` / `${var1}`, `{Reference}` / `${var2}`, `{RefundRef}` / `${var3}`, `{Amount}` / `${var4}`
*Body*: `Pick&Book: Refund {Status} for booking {Reference}. Refund Ref {RefundRef}. Amount Rs. {Amount}`
*(Provider format: `Pick&Book: Refund ${var1} for booking ${var2}. Refund Ref ${var3}. Amount Rs. ${var4}`)*
*Sample Content*: `Pick&Book: Refund credited for booking PNB26091000125. Refund Ref REF-CAN-PNB26091000125. Amount Rs. 1250.00`

---

### 5.5 Refund Failed
**TemplateKey**: `REFUND_FAILED`
**Variables**: `{Amount}`, `{BookingId}`

**Email Template**
*Subject*: Action Required: Refund Failed
*Body*:
```html
<h3>Refund Processing Failed</h3>
<p>We encountered an issue while processing your refund of INR {Amount} for Booking ID {BookingId}.</p>
<p>Our support team will contact you shortly.</p>
```

**SMS Template**
*TemplateKey*: `REFUND_FAILED`
*DLT Template ID*: `1777178997076571137`
*Provider Label*: `newREFUND_FAILED`
*Provider Template ID*: `1563327`
*Sender Header*: `PICNBK`
*Variables*: `{Status}` / `${var1}`, `{Reference}` / `${var2}`, `{RefundRef}` / `${var3}`, `{SupportUrl}` / `${var4}`
*Body*: `Pick&Book: Refund {Status} for booking {Reference}. Refund Ref {RefundRef}.Please contact support : {SupportUrl}`
*(Provider format: `Pick&Book: Refund ${var1} for booking ${var2}. Refund Ref ${var3}.Please contact support : ${var4}`)*
*Sample Content*: `Pick&Book: Refund Failed for booking PNB26091000125. Refund Ref cf_ref_12345.Please contact support : https://www.picknbook.in/contact`
