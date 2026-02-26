document.addEventListener('DOMContentLoaded', function () {
    // Confirmation dialogs for destructive actions
    document.querySelectorAll('[data-confirm]').forEach(function (el) {
        el.addEventListener('click', function (e) {
            if (!confirm(this.getAttribute('data-confirm'))) {
                e.preventDefault();
                e.stopPropagation();
            }
        });
    });

    // Auto-dismiss alerts after 6 seconds
    document.querySelectorAll('.alert-dismissible').forEach(function (alert) {
        setTimeout(function () {
            alert.style.transition = 'opacity 0.3s ease-out';
            alert.style.opacity = '0';
            setTimeout(function () { alert.remove(); }, 300);
        }, 6000);
    });

    // Close alert button
    document.querySelectorAll('.btn-close').forEach(function (btn) {
        btn.addEventListener('click', function () {
            var alert = this.closest('.alert');
            if (alert) {
                alert.style.transition = 'opacity 0.2s ease-out';
                alert.style.opacity = '0';
                setTimeout(function () { alert.remove(); }, 200);
            }
        });
    });

    // Navbar toggle for mobile
    var toggler = document.querySelector('.navbar-toggler');
    var collapse = document.querySelector('#navbarContent');
    if (toggler && collapse) {
        toggler.addEventListener('click', function () {
            collapse.classList.toggle('show');
        });
    }

    // Dropdown toggle (pure JS, no Bootstrap JS needed)
    document.querySelectorAll('.dropdown-toggle').forEach(function (toggle) {
        toggle.addEventListener('click', function (e) {
            e.preventDefault();
            e.stopPropagation();
            var parent = this.closest('.nav-item');
            var wasOpen = parent.classList.contains('show');
            // Close all dropdowns first
            document.querySelectorAll('.nav-item.show').forEach(function (item) {
                item.classList.remove('show');
            });
            if (!wasOpen) {
                parent.classList.add('show');
            }
        });
    });

    // Close dropdowns when clicking outside
    document.addEventListener('click', function (e) {
        if (!e.target.closest('.nav-item')) {
            document.querySelectorAll('.nav-item.show').forEach(function (item) {
                item.classList.remove('show');
            });
        }
    });

    // Announcement popup system — only runs when user is authenticated (navbar present)
    (function() {
        if (!document.getElementById('mainNavbar')) return;

        var token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;

        // Fetch unread count for badge
        fetch('/Announcement/UnreadCount')
            .then(function(r) { if (!r.ok) throw new Error(); return r.json(); })
            .then(function(data) {
                if (data.count > 0) {
                    var badges = document.querySelectorAll('#unreadBadge, #navUnreadBadge');
                    badges.forEach(function(b) {
                        b.textContent = data.count;
                        b.style.display = '';
                    });
                }
            }).catch(function() {});

        // Fetch popup announcements
        fetch('/Announcement/GetPopupAnnouncements')
            .then(function(r) { if (!r.ok) throw new Error(); return r.json(); })
            .then(function(announcements) {
                if (!announcements || announcements.length === 0) return;

                var modal = document.getElementById('announcementModal');
                var body = document.getElementById('announcementModalBody');
                var counter = document.getElementById('announcementCounter');
                var prevBtn = document.getElementById('prevAnnouncement');
                var nextBtn = document.getElementById('nextAnnouncement');
                var closeBtn = document.getElementById('closeAnnouncementModal');
                var backdrop = document.querySelector('.announcement-modal-backdrop');
                if (!modal) return;

                var current = 0;
                function showAnnouncement(idx) {
                    var a = announcements[idx];
                    body.innerHTML = '';
                    var h4 = document.createElement('h4');
                    h4.textContent = a.title;
                    var p = document.createElement('p');
                    p.style.whiteSpace = 'pre-line';
                    p.textContent = a.message;
                    var footer = document.createElement('div');
                    footer.className = 'small text-muted mt-2';
                    footer.textContent = 'From: ' + a.createdBy + ' \u2014 ' + a.createdAt;
                    body.appendChild(h4);
                    body.appendChild(p);
                    body.appendChild(footer);
                    counter.textContent = (idx + 1) + ' of ' + announcements.length;
                    prevBtn.style.display = idx > 0 ? '' : 'none';
                    nextBtn.textContent = idx < announcements.length - 1 ? 'Next' : 'Got it';
                }

                function closeModal() {
                    modal.style.display = 'none';
                    document.body.style.overflow = '';
                    var ids = announcements.map(function(a) { return a.id; });
                    if (token) {
                        fetch('/Announcement/MarkPopupShown', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
                            body: JSON.stringify({ announcementIds: ids })
                        }).catch(function() {});
                    }
                }

                prevBtn.addEventListener('click', function() {
                    if (current > 0) { current--; showAnnouncement(current); }
                });
                nextBtn.addEventListener('click', function() {
                    if (current < announcements.length - 1) { current++; showAnnouncement(current); }
                    else { closeModal(); }
                });
                closeBtn.addEventListener('click', closeModal);
                backdrop.addEventListener('click', closeModal);

                showAnnouncement(0);
                modal.style.display = '';
                document.body.style.overflow = 'hidden';
            }).catch(function() {});
    })();

    // AJAX inline creation for master data dropdowns
    document.querySelectorAll('[data-inline-create]').forEach(function(btn) {
        btn.addEventListener('click', function(e) {
            e.preventDefault();
            var selectId = this.getAttribute('data-target-select');
            var endpoint = this.getAttribute('data-endpoint');
            var selectEl = document.getElementById(selectId);
            var label = this.getAttribute('data-label') || 'item';
            var newValue = prompt('Enter new ' + label + ' name:');
            if (!newValue || newValue.trim() === '') return;
            
            var token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            fetch(endpoint, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': token
                },
                body: JSON.stringify({ Name: newValue.trim() })
            })
            .then(function(res) { return res.json(); })
            .then(function(data) {
                if (data.success) {
                    var option = document.createElement('option');
                    option.value = data.id;
                    option.text = data.name;
                    option.selected = true;
                    selectEl.appendChild(option);
                } else {
                    alert(data.message || 'Error creating item');
                }
            })
            .catch(function(err) {
                console.error(err);
                alert('Error creating item');
            });
        });
    });
});
