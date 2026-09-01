# Notification Templates Catalogue

This document defines all the notification templates (Email, SMS, and WhatsApp) used in the PickNBook transactional notification system. Each template is identified by a unique `TemplateKey` and expects a specific set of variables (payload) to render correctly.

## 1. Authentication (OTPs)

### 1.1 Registration OTP
**TemplateKey**: `REGISTRATION_OTP`
**Variables**: `{OtpCode}`

**Email Template**
*Subject*: Welcome to PickNBook! Verify your email
*Body*:
```html
<h3>Welcome to PickNBook!</h3>
<p>Your one-time registration code is: <strong>{OtpCode}</strong></p>
<p>This code expires in 5 minutes.</p>
```

**SMS/WhatsApp Template**
*Body*: `Welcome to PickNBook! Your registration OTP is {OtpCode}. It is valid for 5 mins.`

---

### 1.2 Login OTP
**TemplateKey**: `LOGIN_OTP`
**Variables**: `{OtpCode}`

**Email Template**
*Subject*: PickNBook Login Verification
*Body*:
```html
<h3>Login Verification</h3>
<p>Your login OTP is: <strong>{OtpCode}</strong></p>
<p>If you did not request this, please secure your account.</p>
```

**SMS/WhatsApp Template**
*Body*: `Your PickNBook login OTP is {OtpCode}. Do not share this with anyone.`

---

### 1.3 Password Reset OTP (User & B2B)
**TemplateKey**: `PASSWORD_RESET_OTP`
**Variables**: `{OtpCode}`

**Email Template**
*Subject*: PickNBook Password Reset Request
*Body*:
```html
<h3>Password Reset</h3>
<p>You requested to reset your password. Your OTP is: <strong>{OtpCode}</strong></p>
<p>If you did not request this, ignore this email.</p>
```

**SMS/WhatsApp Template**
*Body*: `PickNBook password reset OTP is {OtpCode}. Valid for 5 mins.`

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

**SMS/WhatsApp Template**
*TemplateKey*: `FLIGHT_BOOKING_CONFIRMED_SMS`
*Variables*: `{Pnr}`, `{Name}`
*Body*: `Dear {Name}, your flight booking is confirmed! PNR: {Pnr}. Thank you for booking with PickNBook.`

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

**SMS/WhatsApp Template**
*Body*: `Your PickNBook flight booking failed. Any deducted amount will be refunded. Reason: {Reason}.`

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

**SMS/WhatsApp Template**
*TemplateKey*: `HOTEL_BOOKING_CONFIRMED_SMS`
*Variables*: `{HotelName}`, `{Name}`
*Body*: `Dear {Name}, your hotel booking at {HotelName} is confirmed! Check your email for the voucher. PickNBook.`

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

**SMS/WhatsApp Template**
*Body*: `Your PickNBook hotel booking failed. Any deducted amount will be refunded. Reason: {Reason}.`

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

**SMS/WhatsApp Template**
*TemplateKey*: `BUS_BOOKING_CONFIRMED_SMS`
*Variables*: `{Pnr}`, `{Name}`
*Body*: `Dear {Name}, your bus booking is confirmed! PNR: {Pnr}. Thank you for using PickNBook.`

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

**SMS/WhatsApp Template**
*Body*: `Your PickNBook bus booking failed. Any deducted amount will be refunded. Reason: {Reason}.`

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

**SMS/WhatsApp Template**
*Body*: `PickNBook received your payment of INR {Amount} for Order {OrderId}. Booking in progress.`

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

**SMS/WhatsApp Template**
*Body*: `PickNBook payment of INR {Amount} failed (Order: {OrderId}). Reason: {Reason}.`

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

**SMS/WhatsApp Template**
*Body*: `Your PickNBook refund of INR {Amount} for booking ({BookingId}) is successfully completed.`

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

**SMS/WhatsApp Template**
*Body*: `PickNBook refund of INR {Amount} for booking {BookingId} failed. Our support team will contact you.`
